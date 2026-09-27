using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Wandur.Core.Terminal;

/// <summary>Why a link was not offered.</summary>
public enum WebLinkRefusal
{
    None,
    /// <summary>Not a plain web address: another scheme, relative, malformed, too long, user info in it, hidden or
    /// look-alike characters, or a host that is not plain ASCII.</summary>
    NotAWebLink,
    /// <summary>A web address for this computer or a private network: loopback, link-local, private ranges, localhost.</summary>
    LocalNetwork
}

/// <summary>
/// Which links in server text the client may open. The server controls both the link and the text around it, so
/// only plain web addresses on the public internet qualify: absolute http or https, never with a user name or
/// password in the address (<c>https://bank.example@evil.example/</c> reads as one site and goes to another), no
/// invisible or direction-changing characters (a right-to-left override makes <c>gnp.exe</c> read as <c>exe.png</c>),
/// and a host that is plain ASCII, so a Cyrillic letter or a fullwidth dot cannot pass for a familiar name. Hosts on
/// this computer or a private network are refused too: a world has no business sending the reader to a router's admin
/// page. Opening still needs the reader's confirmation.
/// </summary>
public static class WebLinks
{
    public const int MaximumLength = 2048;

    /// <summary>The address to open, or null when the link must not be opened.</summary>
    public static Uri? Accept(string? url) => Check(url).Uri;

    /// <summary>The address to open, or null and the reason it is refused.</summary>
    public static (Uri? Uri, WebLinkRefusal Refusal) Check(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (null, WebLinkRefusal.NotAWebLink);
        url = url.Trim();
        if (url.Length > MaximumLength || url.Any(IsHidden)) return (null, WebLinkRefusal.NotAWebLink);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return (null, WebLinkRefusal.NotAWebLink);
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return (null, WebLinkRefusal.NotAWebLink);
        // The same characters written as percent escapes (%E2%80%AE is a right-to-left override) show decoded in a
        // browser's address bar or history, so they are refused too. An escaped space is ordinary and allowed.
        if (Uri.UnescapeDataString(uri.AbsoluteUri).Any(c => c != ' ' && IsHidden(c))) return (null, WebLinkRefusal.NotAWebLink);
        if (uri.UserInfo.Length > 0 || Authority(url).Contains('@', StringComparison.Ordinal)) return (null, WebLinkRefusal.NotAWebLink);
        // The address as written must name its host in ASCII, and the parsed host must be ASCII and already its
        // IDN form: this refuses homographs, punycode look-alikes spelled in Unicode, and fullwidth dots.
        if (string.IsNullOrEmpty(uri.Host) || !Ascii(Authority(url)) || !Ascii(uri.Host) ||
            !string.Equals(uri.Host.Trim('[', ']'), uri.IdnHost.Trim('[', ']'), StringComparison.OrdinalIgnoreCase)) return (null, WebLinkRefusal.NotAWebLink);
        if (IsLocal(uri)) return (null, WebLinkRefusal.LocalNetwork);
        return (uri, WebLinkRefusal.None);
    }

    /// <summary>Characters that draw nothing, change direction, or are not text: format (bidi controls, zero-width
    /// characters), control, private use, surrogate, unassigned, and line or paragraph separators, plus spaces.</summary>
    private static bool IsHidden(char c) => char.IsWhiteSpace(c) || CharUnicodeInfo.GetUnicodeCategory(c) is
        UnicodeCategory.Format or UnicodeCategory.Control or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate or
        UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    private static bool Ascii(string text) => text.All(c => c is > ' ' and < '\u007f');

    /// <summary>The text between "//" and the first '/', '?', '#' or '\', as written.</summary>
    private static string Authority(string url)
    {
        var start = url.IndexOf("//", StringComparison.Ordinal);
        if (start < 0) return "";
        start += 2;
        var end = url.IndexOfAny(['/', '?', '#', '\\'], start);
        return end < 0 ? url[start..] : url[start..end];
    }

    private static bool IsLocal(Uri uri)
    {
        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        if (host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal)) return true;
        // A trailing dot makes .NET class a numeric host as a DNS name (127.0.0.1. and 169.254.169.254. are Dns), yet
        // it still reaches the address, so the host is classified by whether it parses as an address, not by type.
        if (!IPAddress.TryParse(host.Trim('[', ']'), out var address))
            return uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] is 0 or 10 or 127 || (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] is >= 16 and <= 31) ||
                (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and <= 127);
        }
        var bytes = address.GetAddressBytes();
        return address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal ||
            (bytes[0] & 0xFE) == 0xFC;
    }
}
