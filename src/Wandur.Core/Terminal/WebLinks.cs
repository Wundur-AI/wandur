namespace Wandur.Core.Terminal;

/// <summary>
/// Which links in server text the client may open. The server controls both the link and the text around it, so
/// only plain web addresses qualify: absolute http or https, with a host, and never with a user name or password
/// in the address (<c>https://bank.example@evil.example/</c> reads as one site and goes to another). Everything
/// else, such as <c>file:</c>, <c>javascript:</c>, <c>mailto:</c> or a custom scheme, is refused. Opening still
/// needs the reader's confirmation.
/// </summary>
public static class WebLinks
{
    public const int MaximumLength = 2048;

    /// <summary>The address to open, or null when the link must not be opened.</summary>
    public static Uri? Accept(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (url.Length > MaximumLength || url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (uri.UserInfo.Length > 0 || (url.Contains('@', StringComparison.Ordinal) && HasUserInfo(url))) return null;
        if (string.IsNullOrEmpty(uri.Host)) return null;
        return uri;
    }

    /// <summary>An '@' before the first '/', '?' or '#' after the scheme's "//" is user info, however the parser read it.</summary>
    private static bool HasUserInfo(string url)
    {
        var start = url.IndexOf("//", StringComparison.Ordinal);
        if (start < 0) return false;
        start += 2;
        var end = url.IndexOfAny(['/', '?', '#', '\\'], start);
        var authority = end < 0 ? url[start..] : url[start..end];
        return authority.Contains('@', StringComparison.Ordinal);
    }
}
