namespace Wandur.Core.Diagnostics;

/// <summary>
/// Opt-in trace of every artwork fetch: the address, the status, the bytes and any error. Off unless
/// <c>WANDUR_ART_TRACE</c> names a writable file, the same way <see cref="ThemeTrace"/> works. Without it a picture
/// that never arrives leaves only the initials plate behind, which says nothing about why.
/// </summary>
public static class ArtTrace
{
    private static readonly Lock Gate = new();
    private static readonly string? Path = Resolve();
    public static bool Enabled => Path is not null;
    /// <summary>Every line also goes here, whether or not the file trace is on, so tests can read it.</summary>
    public static event Action<string>? Written;

    private static string? Resolve()
    {
        var path = Environment.GetEnvironmentVariable("WANDUR_ART_TRACE");
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static void Write(string url, int status, long bytes, string? error = null)
    {
        var detail = $"{status} {bytes} bytes {url}{(error is null ? "" : "  error " + error)}";
        Written?.Invoke(detail);
        if (Path is null) return;
        var line = $"{DateTime.Now:HH:mm:ss.fff}  art  {detail}{Environment.NewLine}";
        try { lock (Gate) File.AppendAllText(Path, line); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { }
    }
}
