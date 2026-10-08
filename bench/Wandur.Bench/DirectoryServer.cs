using System.Net;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace Wandur.Bench;

/// <summary>Large, original test artwork drawn locally: gradients, shapes and fine texture, so codecs have real work.</summary>
public static class Artwork
{
    public static byte[] Generate(int width, int height, SKEncodedImageFormat format, int seed = 7)
    {
        var random = new Random(seed);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        using (var gradient = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
                [new SKColor(20, 30, 70), new SKColor(140, 70, 160), new SKColor(240, 180, 90)], SKShaderTileMode.Clamp)
        })
            canvas.DrawRect(0, 0, width, height, gradient);
        using var paint = new SKPaint { IsAntialias = true };
        for (var i = 0; i < 400; i++)
        {
            paint.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(40, 200));
            canvas.DrawCircle(random.Next(width), random.Next(height), random.Next(10, Math.Max(11, width / 10)), paint);
        }
        // Fine texture so the encoders cannot collapse large flat areas.
        for (var i = 0; i < width * height / 400; i++)
        {
            paint.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 60);
            canvas.DrawRect(random.Next(width), random.Next(height), 3, 3, paint);
        }
        using var image = surface.Snapshot();
        using var data = image.Encode(format, 88);
        return data.ToArray();
    }
}

/// <summary>
/// A loopback-only stand-in for the wandur.net directory: GET /directory lists <c>count</c> generated worlds built on the
/// repository's fixture, and every world's art (any size asked for) is the same large generated picture, JPEG for
/// most worlds and PNG for every fifth, so thumbnail decoding sees full-size sources. Counts art requests.
/// </summary>
public sealed class DirectoryServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly byte[] _directory;
    private readonly byte[] _jpeg;
    private readonly byte[] _png;
    private readonly Task _loop;
    private int _artRequests;

    public DirectoryServer(int port, int count, byte[] jpeg, byte[] png, string fixturePath)
    {
        _jpeg = jpeg; _png = png;
        var fixture = JsonNode.Parse(File.ReadAllText(fixturePath))!.AsObject();
        var template = fixture["worlds"]!.AsArray()[0]!;
        var worlds = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            var world = template.DeepClone().AsObject();
            world["id"] = $"bench-world-{i}";
            world["name"] = $"Bench World {i:D3}";
            world["summary"] = $"Generated directory entry {i} for the performance harness";
            world["host"] = $"bench{i}.invalid";
            world["port"] = 4000 + i;
            world["tls_port"] = null;
            world["generated_artwork_path"] = $"worlds/bench-world-{i}/art";
            world["banner_url"] = "";
            worlds.Add(world);
        }
        fixture["worlds"] = worlds;
        fixture["fetched_at"] = DateTimeOffset.UtcNow.ToString("O");
        _directory = System.Text.Encoding.UTF8.GetBytes(fixture.ToJsonString());
        Port = port;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _loop = Task.Run(LoopAsync);
    }

    public int Port { get; }
    public string Address => $"http://127.0.0.1:{Port}";
    public int ArtRequests => Volatile.Read(ref _artRequests);

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(() => Serve(context));
        }
    }

    private void Serve(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url!.AbsolutePath.Trim('/');
            byte[] body;
            if (path == "directory") { context.Response.ContentType = "application/json"; body = _directory; }
            else if (path.StartsWith("worlds/", StringComparison.Ordinal) && path.EndsWith("/art", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _artRequests);
                var index = int.TryParse(path.Split('/')[1].Replace("bench-world-", ""), out var n) ? n : 0;
                var png = index % 5 == 4;
                context.Response.ContentType = png ? "image/png" : "image/jpeg";
                body = png ? _png : _jpeg;
            }
            else { context.Response.StatusCode = 404; body = []; }
            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body);
            context.Response.Close();
        }
        catch { try { context.Response.Abort(); } catch { } }
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
        try { _loop.Wait(1000); } catch { }
    }
}
