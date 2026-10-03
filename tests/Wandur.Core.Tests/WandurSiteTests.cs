using Wandur.Core.Discovery;

namespace Wandur.Core.Tests;

/// <summary>The wandur.net pages the client opens follow the directory's configuration, so a development build or a
/// test run never opens a production page it did not ask for.</summary>
public sealed class WandurSiteTests
{
    [Theory]
    [InlineData(null, null, "https://www.wandur.net/")]
    [InlineData("", "  ", "https://www.wandur.net/")]
    [InlineData(null, "http://127.0.0.1:5199", "http://127.0.0.1:5199/")]
    [InlineData("https://staging.example.org/", "http://127.0.0.1:5199", "https://staging.example.org/")]
    [InlineData("not an address", "http://127.0.0.1:9/", "http://127.0.0.1:9/")]
    [InlineData("ftp://example.org", null, "https://www.wandur.net/")]
    public void TheSiteFollowsItsOwnSettingThenTheDirectorysThenThePublicSite(string? site, string? directory, string expected) =>
        Assert.Equal(new Uri(expected), WandurSite.Resolve(site, directory));

    [Fact]
    public void ThePagesSitUnderTheBaseAddress()
    {
        // The test assembly points WANDUR_DIRECTORY_URL at a closed loopback port, so the pages follow it there.
        var root = WandurSite.BaseUri;
        Assert.Equal(new Uri(root, "client/help"), WandurSite.Help);
        Assert.Equal(new Uri(root, "clients"), WandurSite.OtherClients);
        Assert.Equal("https://www.wandur.net/client/help", new Uri(WandurSite.Resolve(null, null), "client/help").AbsoluteUri);
    }
}
