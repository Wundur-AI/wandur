using Wandur.Core.Settings;

namespace Wandur.Core.Discovery;

/// <summary>
/// The anonymous install id (<see cref="ClientSettings.InstallId"/>): a random GUID kept in the settings, sent as its own
/// <see cref="Header"/> on requests to wandur.net so the site can count installs, and never in the User-Agent. It goes
/// only to wandur.net (https, any subdomain) and to the directory address the client is configured with
/// (<c>WANDUR_DIRECTORY_URL</c>), never to a banner host, a MUD server, an agent model server or The Mud Connector, and
/// not at all when <see cref="ClientSettings.SendInstallId"/> is off. Even there it goes only on the two requests the
/// site counts installs from (<see cref="IsCounted"/>): the world list and the update check, not on artwork or theme
/// images.
/// </summary>
public static class InstallIdentity
{
    public const string Header = "X-Wandur-Install";

    /// <summary>The header value these settings allow, or null when the setting is off or there is no id yet.</summary>
    public static string? HeaderValue(ClientSettings settings) =>
        settings is { SendInstallId: true, InstallId: { } id } && id != Guid.Empty ? id.ToString("D") : null;

    /// <summary>The settings to save: the stored id when there is one, else the id they carry, else a new one.</summary>
    public static ClientSettings Keep(ClientSettings settings, Guid? stored)
    {
        var id = stored is { } kept && kept != Guid.Empty ? kept
            : settings.InstallId is { } own && own != Guid.Empty ? own : Guid.NewGuid();
        return settings.InstallId == id ? settings : settings with { InstallId = id };
    }

    /// <summary>Whether a request to this address is a request to wandur.net: an https address on wandur.net or one of
    /// its subdomains on the default port, or the same scheme, host and port as the configured directory.</summary>
    public static bool IsWandurNet(Uri address, Uri directoryBase)
    {
        if (!address.IsAbsoluteUri || address.UserInfo.Length > 0) return false;
        var host = address.IdnHost.TrimEnd('.');
        if (address.Scheme == Uri.UriSchemeHttps && address.IsDefaultPort
            && (host.Equals("wandur.net", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".wandur.net", StringComparison.OrdinalIgnoreCase)))
            return true;
        return directoryBase.IsAbsoluteUri && directoryBase.UserInfo.Length == 0
            && Uri.Compare(address, directoryBase, UriComponents.SchemeAndServer, UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) == 0;
    }

    /// <summary>Whether a request to this address is one the site counts installs from: the world list
    /// (<c>/directory</c>) or the update check (<c>/client/latest</c>), under the directory's base path.</summary>
    public static bool IsCounted(Uri address)
    {
        if (!address.IsAbsoluteUri) return false;
        var path = address.AbsolutePath.TrimEnd('/');
        return path.EndsWith("/directory", StringComparison.Ordinal) || path.EndsWith("/client/latest", StringComparison.Ordinal);
    }

    /// <summary>A handler for the client's own wandur.net HttpClients that adds the header where it belongs. It follows
    /// redirects itself, so the header is decided again for every address and never rides a redirect to another host.</summary>
    public static HttpMessageHandler CreateHandler(Uri directoryBase, InstallHeader install) =>
        new InstallHeaderHandler(directoryBase, install) { InnerHandler = new HttpClientHandler { AllowAutoRedirect = false } };

    private sealed class InstallHeaderHandler(Uri directoryBase, InstallHeader install) : DelegatingHandler
    {
        private const int MaxRedirects = 10;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            for (var hop = 0; ; hop++)
            {
                request.Headers.Remove(Header);
                if (request.RequestUri is { } address && IsWandurNet(address, directoryBase) && IsCounted(address) && install.Value is { } id)
                    request.Headers.TryAddWithoutValidation(Header, id);
                var response = await base.SendAsync(request, cancellationToken);
                if (hop >= MaxRedirects || Next(request, response) is not { } next) return response;
                response.Dispose();
                // Re-sending the same request object is safe only because Next follows GET and HEAD, which carry no
                // body. Do not extend it to POST or PUT: their content is consumed by the first send.
                request.RequestUri = next;
            }
        }

        /// <summary>Where a redirect of a GET or HEAD request points, or null when it is not one to follow (no https to http).</summary>
        private static Uri? Next(HttpRequestMessage request, HttpResponseMessage response)
        {
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308) || request.Method != HttpMethod.Get && request.Method != HttpMethod.Head
                || response.Headers.Location is not { } location || request.RequestUri is not { } current) return null;
            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (next.Scheme is not ("http" or "https") || current.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp) return null;
            return next;
        }
    }
}

/// <summary>The header value in use, shared by the client's wandur.net HttpClients and updated when the settings change.</summary>
public sealed class InstallHeader
{
    private volatile string? _value;
    public string? Value => _value;
    public void Apply(ClientSettings settings) => _value = InstallIdentity.HeaderValue(settings);
}
