using Wandur.Core.Terminal;

namespace Wandur.Core.Tests;

public sealed class WebLinksTests
{
    [Theory]
    [InlineData("https://www.wandur.net/worlds?id=1#top")]
    [InlineData("http://example.test:4000/")]
    [InlineData("HTTPS://Example.Test/Path")]
    public void PlainWebAddressesAreAccepted(string url) => Assert.NotNull(WebLinks.Accept(url));

    /// <summary>An escaped space, and escaped ordinary text, are still plain web addresses.</summary>
    [Theory]
    [InlineData("https://example.com/some%20page")]
    [InlineData("https://example.com/caf%C3%A9")]
    public void OrdinaryPercentEscapesAreAccepted(string url) => Assert.NotNull(WebLinks.Accept(url));

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.test")]
    [InlineData("ftp://example.test/")]
    [InlineData("ssh://example.test")]
    [InlineData("send:kill orc")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("//example.test/relative")]
    [InlineData("/relative/path")]
    [InlineData("")]
    [InlineData("   ")]
    public void OtherSchemesAndRelativeLinksAreRefused(string url) => Assert.Null(WebLinks.Accept(url));

    [Theory]
    [InlineData("https://user@example.test/")]
    [InlineData("https://user:secret@example.test/")]
    [InlineData("https://bank.example@evil.example/login")]
    [InlineData("http://:@example.test/")]
    public void AddressesWithUserInfoAreRefused(string url) => Assert.Null(WebLinks.Accept(url));

    [Fact]
    public void AnAtSignAfterTheHostIsFine() => Assert.NotNull(WebLinks.Accept("https://example.test/users/@someone?mail=a@b"));

    [Fact]
    public void ControlCharactersSpacesAndHugeAddressesAreRefused()
    {
        Assert.Null(WebLinks.Accept("https://example.test/a\u0007b"));
        Assert.Null(WebLinks.Accept("https://example.test/a b"));
        Assert.Null(WebLinks.Accept("https://example.test/" + new string('a', WebLinks.MaximumLength)));
    }

    public static TheoryData<string> Spoofs => new()
    {
        "https://wandur.net\u202egnp.exe",          // right-to-left override: reads as exe.png
        "https://\u0430pple.com/",                  // Cyrillic a
        "https://app\u200ble.com/",                 // zero-width space in the host
        "https://example\uff0ecom/",                // fullwidth dot
        "https://example.com/\u2066path",           // bidi isolate in the path
        "https://example.com/\ue000",               // private use
        "https://example.com/a\u2028b",             // line separator
        "https://example.com/%E2%80%AEgnp.exe",     // right-to-left override written as a percent escape
        "https://example.com/a%E2%80%8Bb",          // zero-width space written as a percent escape
        "https://xn--pple-43d.com/\u0301",          // combining mark is fine, but the unassigned below is not
    };

    [Theory]
    [MemberData(nameof(Spoofs))]
    public void HiddenAndLookAlikeCharactersAreRefused(string url)
    {
        if (url.EndsWith("\u0301", StringComparison.Ordinal)) url = url[..^1] + "\u0378"; // U+0378 is unassigned
        Assert.Equal(WebLinkRefusal.NotAWebLink, WebLinks.Check(url).Refusal);
    }

    [Fact]
    public void ThePunycodeFormOfAHostIsAPlainAsciiHost()
    {
        var uri = WebLinks.Accept("https://xn--pple-43d.com/");
        Assert.Equal("https://xn--pple-43d.com/", uri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://LOCALHOST:8080/admin")]
    [InlineData("http://printer.localhost/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.1.2.3/")]
    [InlineData("http://0x7f.1/")]
    [InlineData("http://2130706433/")]
    [InlineData("http://0.0.0.0/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://172.16.5.4/")]
    [InlineData("http://172.31.255.255/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://100.64.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fd12:3456::1]/")]
    [InlineData("http://[::ffff:192.168.0.1]/")]
    [InlineData("http://127.0.0.1./")]
    [InlineData("http://192.168.1.1./admin")]
    [InlineData("http://10.0.0.1./")]
    [InlineData("http://0x7f.1./")]
    [InlineData("http://2130706433./")]
    [InlineData("http://169.254.169.254./latest/meta-data")]
    public void LoopbackLinkLocalAndPrivateHostsAreRefused(string url) => Assert.Equal(WebLinkRefusal.LocalNetwork, WebLinks.Check(url).Refusal);

    [Theory]
    [InlineData("http://172.32.0.1/")]
    [InlineData("http://8.8.8.8/")]
    [InlineData("http://[2606:4700::1111]/")]
    [InlineData("https://localhost.example.com/")]
    public void PublicHostsAreAccepted(string url) => Assert.NotNull(WebLinks.Accept(url));
}
