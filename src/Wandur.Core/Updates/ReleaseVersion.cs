using System.Globalization;
using System.Text.RegularExpressions;

namespace Wandur.Core.Updates;

/// <summary>
/// A release version as the client and wandur.net spell it (0.1.5, 0.1.6-rc.2), compared by semantic version rules:
/// numbers compare as numbers (0.1.10 is newer than 0.1.9), and a prerelease comes before its release (0.1.5-rc.2 is
/// older than 0.1.5). Build metadata after a plus sign is ignored. 0.0.0 with any suffix is a build from source.
/// </summary>
public sealed partial class ReleaseVersion : IComparable<ReleaseVersion>
{
    private ReleaseVersion(int major, int minor, int patch, string[] prerelease)
    {
        Major = major; Minor = minor; Patch = patch; Prerelease = prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public IReadOnlyList<string> Prerelease { get; }
    public bool IsPrerelease => Prerelease.Count > 0;
    /// <summary>A build from source reports 0.0.0-dev (Directory.Build.props); no release is ever numbered 0.0.0.</summary>
    public bool IsDevelopment => Major == 0 && Minor == 0 && Patch == 0;

    public static bool TryParse(string? text, out ReleaseVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64) return false;
        var value = text.Trim();
        if (value.StartsWith('v')) value = value[1..];
        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0) value = value[..plus];
        var match = Shape().Match(value);
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return false;
        var prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        version = new ReleaseVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var core = Major != other.Major ? Major.CompareTo(other.Major)
            : Minor != other.Minor ? Minor.CompareTo(other.Minor)
            : Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        // A release outranks any prerelease of the same number.
        if (IsPrerelease != other.IsPrerelease) return IsPrerelease ? -1 : 1;
        if (!IsPrerelease) return 0;
        for (var i = 0; i < Math.Min(Prerelease.Count, other.Prerelease.Count); i++)
        {
            var compared = CompareIdentifier(Prerelease[i], other.Prerelease[i]);
            if (compared != 0) return compared;
        }
        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    /// <summary>Numeric identifiers compare as numbers and sort before alphanumeric ones, which compare as text.</summary>
    private static int CompareIdentifier(string a, string b)
    {
        var aNumeric = a.All(char.IsAsciiDigit);
        var bNumeric = b.All(char.IsAsciiDigit);
        if (aNumeric && bNumeric)
        {
            var x = a.TrimStart('0');
            var y = b.TrimStart('0');
            return x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
        }
        if (aNumeric) return -1;
        if (bNumeric) return 1;
        return Math.Sign(string.CompareOrdinal(a, b));
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (IsPrerelease ? "-" + string.Join('.', Prerelease) : "");

    [GeneratedRegex(@"^([0-9]{1,9})\.([0-9]{1,9})\.([0-9]{1,9})(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
