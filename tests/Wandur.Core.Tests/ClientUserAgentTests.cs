using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Wandur.Core.Discovery;

namespace Wandur.Core.Tests;

public sealed class ClientUserAgentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wandur-useragent-" + Guid.NewGuid());

    [Fact]
    public void TheValueNamesTheProductVersionSystemAndArchitectureOnly()
    {
        Assert.Equal("WandurMudClient/0.1.3 (macOS; arm64)", ClientUserAgent.Build("0.1.3", "macOS", Architecture.Arm64));
        Assert.Equal("WandurMudClient/0.1.4-rc.1 (Windows; x64)", ClientUserAgent.Build("0.1.4-rc.1", "Windows", Architecture.X64));
        // Anything that could break the header or smuggle text in is dropped.
        Assert.Equal("WandurMudClient/1.0evil (Linux; x64)", ClientUserAgent.Build("1.0 (evil)\r\n", "Linux", Architecture.X64));
        Assert.Matches(@"^WandurMudClient/[0-9A-Za-z.\-_]+ \((macOS|Windows|Linux|Other); [a-z0-9]+\)$", ClientUserAgent.Value);
    }

    [Fact]
    public async Task TheCatalogsOwnClientSendsItToTheDirectory()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var head = new StringBuilder();
            var buffer = new byte[4096];
            while (!head.ToString().Contains("\r\n\r\n"))
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0) break;
                head.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { schema_version = 1, fetched_at = DateTimeOffset.UtcNow, games = Array.Empty<object>() }));
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
            await stream.WriteAsync(body);
            return head.ToString();
        });

        using var catalog = new WorldCatalog(Path.Combine(_dir, "directory.json"), new Uri($"http://127.0.0.1:{port}/"));
        await catalog.LoadAsync(force: true);

        var request = await serve.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.StartsWith("GET /directory ", request);
        Assert.Contains($"User-Agent: {ClientUserAgent.Value}\r\n", request);
    }

    [Fact]
    public void TestsNeverReachTheRealDirectory()
    {
        using var catalog = new WorldCatalog(Path.Combine(_dir, "directory.json"));
        Assert.Equal(new Uri(OfflineDirectory.Address), catalog.BaseUri);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
