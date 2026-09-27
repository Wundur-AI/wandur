using Wandur.Core.Terminal;

namespace Wandur.Core.Tests;

public sealed class WebLinksTests
{
    [Theory]
    [InlineData("https://www.wandur.net/worlds?id=1#top")]
    [InlineData("http://example.test:4000/")]
    [InlineData("HTTPS://Example.Test/Path")]
    public void PlainWebAddressesAreAccepted(string url) => Assert.NotNull(WebLinks.Accept(url));

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
}
