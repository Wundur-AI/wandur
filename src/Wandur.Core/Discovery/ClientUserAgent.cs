using System.Reflection;
using System.Runtime.InteropServices;

namespace Wandur.Core.Discovery;

/// <summary>
/// How the client names itself to wandur.net, for example <c>WandurMudClient/0.1.3 (macOS; arm64)</c>: the product,
/// its version, the operating system family and the processor architecture. The site counts requests by it, so the
/// owner can see how many clients fetch the directory each day and which versions they run. Nothing in it identifies
/// a person or an install: no machine name, no user name, no install id, no OS build. The anonymous install id, when it
/// is on, travels in its own header (<see cref="InstallIdentity"/>).
/// </summary>
public static class ClientUserAgent
{
    public const string Product = "WandurMudClient";

    public static string Value { get; } = Build(Version(Assembly.GetEntryAssembly()), OperatingSystemName(), RuntimeInformation.OSArchitecture);

    /// <summary>The header value for a version, system and architecture; the parts are reduced to safe tokens.</summary>
    public static string Build(string version, string system, Architecture architecture) =>
        $"{Product}/{Token(version)} ({Token(system)}; {architecture.ToString().ToLowerInvariant()})";

    /// <summary>The release version (0.1.3, or 0.1.3-rc.4), without the commit hash .NET appends after a plus sign.</summary>
    internal static string Version(Assembly? assembly)
    {
        var informational = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational?.Split('+')[0] ?? assembly?.GetName().Version?.ToString(3);
        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
    }

    private static string OperatingSystemName() =>
        OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Other";

    private static string Token(string value) =>
        new(value.Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_').Take(32).ToArray());
}
