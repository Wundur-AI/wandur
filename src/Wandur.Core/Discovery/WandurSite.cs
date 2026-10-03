namespace Wandur.Core.Discovery;

/// <summary>The wandur.net pages the client links to, such as the online help. The address follows the directory's
/// configuration: <c>WANDUR_SITE_URL</c> when set, else <c>WANDUR_DIRECTORY_URL</c> (a local site loop serves the
/// pages and the API from one address, and the tests point it at a closed loopback port), else the public site.
/// So a development build or a test run never opens a production page it did not ask for.</summary>
public static class WandurSite
{
    public const string PublicAddress = "https://www.wandur.net/";

    /// <summary>The site's base address, with a trailing slash, from the environment as described above.</summary>
    public static Uri BaseUri => Resolve(Environment.GetEnvironmentVariable("WANDUR_SITE_URL"), Environment.GetEnvironmentVariable("WANDUR_DIRECTORY_URL"));

    /// <summary>The online help's home page.</summary>
    public static Uri Help => Page("client/help");

    /// <summary>The site's page about other MUD clients.</summary>
    public static Uri OtherClients => Page("clients");

    public static Uri Page(string path) => new(BaseUri, path.TrimStart('/'));

    /// <summary>The base address from the two settings: the site's own first, then the directory's, then the public
    /// site. A value that is not an absolute http or https address is ignored.</summary>
    public static Uri Resolve(string? site, string? directory)
    {
        foreach (var candidate in new[] { site, directory })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (Uri.TryCreate(candidate.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                return uri;
        }
        return new Uri(PublicAddress);
    }
}
