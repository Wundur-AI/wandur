using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Wandur.Core.Discovery;
using Wandur.Core.Settings;

namespace Wandur.Core.Updates;

/// <summary>What wandur.net says the newest release is: its version, the downloads page and the release notes.</summary>
public sealed record UpdateInfo(string Version, Uri Page, Uri? Notes);

/// <summary>The last update check, kept in the settings so a restart within the day does not ask again. A failed
/// check keeps the version learned before and only moves the time.</summary>
public sealed record UpdateCheckRecord
{
    public DateTimeOffset CheckedAt { get; init; }
    public string? Version { get; init; }
    public string? Page { get; init; }
    public string? Notes { get; init; }
}

public enum UpdateCheckStatus { UpToDate, Available, Failed, Disabled }

/// <summary>The outcome of one check, and the record to save (null when nothing was asked).</summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Latest, UpdateCheckRecord? Record);

/// <summary>Where the newest release comes from: <c>GET /client/latest</c> in the client, a fake in tests.</summary>
public interface IUpdateSource
{
    Task<UpdateInfo> GetLatestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Asks <c>{directory}/client/latest</c>, the same base address the directory uses (<c>WANDUR_DIRECTORY_URL</c>, so
/// tests stay on loopback), with the client's User-Agent. It only reads: nothing is downloaded or installed.
/// </summary>
public sealed class HttpUpdateSource : IUpdateSource, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    public Uri Address { get; }

    public HttpUpdateSource(Uri directoryBase, HttpClient? http = null)
    {
        Address = new Uri(new Uri(directoryBase.AbsoluteUri.TrimEnd('/') + "/"), "client/latest");
        _ownsHttp = http is null;
        if (http is null)
        {
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(ClientUserAgent.Value);
        }
        _http = http;
    }

    public async Task<UpdateInfo> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Address);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return Parse(json);
    }

    /// <summary>Reads the response. The version must be a release version; the downloads page is used only when it is
    /// an https page on wandur.net, and the notes only on GitHub or wandur.net, so the client never opens anywhere else.</summary>
    public static UpdateInfo Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var version = root.GetProperty("version").GetString();
        if (!ReleaseVersion.TryParse(version, out var parsed) || parsed!.IsDevelopment)
            throw new FormatException("The release version could not be read.");
        var page = Text(root, "page") is { } pageText && UpdateService.TrustedPage(pageText) is { } trusted ? trusted : UpdateService.DownloadsPage;
        var notes = Text(root, "notes") is { } notesText ? UpdateService.TrustedNotes(notesText) : null;
        return new UpdateInfo(parsed.ToString(), page, notes);
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}

/// <summary>
/// When to ask about a newer release and what to offer. A release build asks once shortly after startup and then at
/// most once a day; a build from source (0.0.0-dev) never asks and is never offered anything, unless
/// <see cref="OverrideVariable"/> names the release version it should check as. Skipping a version hides that version
/// for good, and a newer one shows again.
/// </summary>
public sealed class UpdateService(IUpdateSource source, TimeProvider time, string runningVersion)
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    /// <summary>Set to a release version (for example 0.1.4) to let a build from source check as that version.</summary>
    public const string OverrideVariable = "WANDUR_UPDATE_CHECK_VERSION";
    public static readonly Uri DownloadsPage = new("https://www.wandur.net/client/downloads");

    public string RunningVersion { get; } = runningVersion;
    public TimeProvider Clock => time;

    /// <summary>False for a build from source, or for a version that cannot be read.</summary>
    public bool ChecksAllowed => ReleaseVersion.TryParse(RunningVersion, out var running) && !running!.IsDevelopment;

    /// <summary>The service for this build: the assembly's own version, or the override when this is a build from source.</summary>
    public static UpdateService ForThisBuild(IUpdateSource source, TimeProvider? time = null) =>
        new(source, time ?? TimeProvider.System, EffectiveVersion(BuildVersion(), Environment.GetEnvironmentVariable(OverrideVariable)));

    /// <summary>The version the check runs as: the build's own, or for a build from source the override when it is a release version.</summary>
    public static string EffectiveVersion(string build, string? overrideVersion) =>
        ReleaseVersion.TryParse(build, out var running) && running!.IsDevelopment
            && ReleaseVersion.TryParse(overrideVersion, out var chosen) && !chosen!.IsDevelopment
            ? chosen.ToString() : build;

    /// <summary>This build's version (0.1.5, 0.1.6-rc.1, or 0.0.0-dev from source), without the commit hash.</summary>
    public static string BuildVersion()
    {
        var assembly = typeof(UpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0-dev";
    }

    /// <summary>Whether an automatic check should run now: checks are on, this is a release build, and the last check is
    /// a day old (or lies in the future, after the clock was set back).</summary>
    public bool IsDue(ClientSettings settings)
    {
        if (!settings.CheckForUpdates || !ChecksAllowed) return false;
        if (settings.LastUpdateCheck is not { } last) return true;
        var now = time.GetUtcNow();
        return now - last.CheckedAt >= Interval || last.CheckedAt > now + TimeSpan.FromMinutes(5);
    }

    /// <summary>Whether <paramref name="version"/> is newer than this build. Never for a build from source.</summary>
    public bool IsNewer(string? version) =>
        ChecksAllowed && ReleaseVersion.TryParse(RunningVersion, out var running)
        && ReleaseVersion.TryParse(version, out var candidate) && candidate!.CompareTo(running) > 0;

    /// <summary>The release to show from the remembered check, or null: nothing newer, the version was skipped, or
    /// automatic checks are off.</summary>
    public UpdateInfo? Offer(ClientSettings settings)
    {
        if (!settings.CheckForUpdates || settings.LastUpdateCheck is not { Version: { } version } last) return null;
        if (!IsNewer(version) || IsSkipped(settings, version)) return null;
        return new UpdateInfo(version, TrustedPage(last.Page) ?? DownloadsPage, TrustedNotes(last.Notes));
    }

    public static bool IsSkipped(ClientSettings settings, string version) =>
        settings.SkippedUpdateVersion is { } skipped && ReleaseVersion.TryParse(skipped, out var a)
        && ReleaseVersion.TryParse(version, out var b) && a!.CompareTo(b) == 0;

    /// <summary>Asks once. No retries: a failure is reported and the time recorded, so the next try is a day later.</summary>
    public async Task<UpdateCheckResult> CheckAsync(ClientSettings settings, CancellationToken cancellationToken = default)
    {
        if (!ChecksAllowed) return new(UpdateCheckStatus.Disabled, null, null);
        try
        {
            var latest = await source.GetLatestAsync(cancellationToken);
            var record = new UpdateCheckRecord
            {
                CheckedAt = time.GetUtcNow(), Version = latest.Version, Page = latest.Page.AbsoluteUri, Notes = latest.Notes?.AbsoluteUri,
            };
            return new(IsNewer(latest.Version) ? UpdateCheckStatus.Available : UpdateCheckStatus.UpToDate, latest, record);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or FormatException
            or InvalidOperationException or KeyNotFoundException or IOException or UriFormatException)
        {
            var record = (settings.LastUpdateCheck ?? new UpdateCheckRecord()) with { CheckedAt = time.GetUtcNow() };
            return new(UpdateCheckStatus.Failed, null, record);
        }
    }

    /// <summary>An https page on wandur.net, or null.</summary>
    public static Uri? TrustedPage(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
        && uri.IsDefaultPort && (uri.Host == "wandur.net" || uri.Host.EndsWith(".wandur.net", StringComparison.Ordinal)) ? uri : null;

    /// <summary>An https page on GitHub or wandur.net, or null.</summary>
    public static Uri? TrustedNotes(string? text) =>
        TrustedPage(text) ?? (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort && uri.Host == "github.com" ? uri : null);
}
