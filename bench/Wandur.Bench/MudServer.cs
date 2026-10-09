using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Wandur.Bench;

/// <summary>
/// A loopback-only stand-in for a MUD: accepts telnet connections on 127.0.0.1, ignores whatever the client sends
/// (its option negotiation included), and streams generated ANSI text at a fixed byte rate. Rate 0 means a short
/// greeting and then a prompt every two seconds, which is what an idle session sees.
/// </summary>
public sealed class MudServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly int _bytesPerSecond;
    private readonly long _limitBytes;
    private readonly bool _chat;
    private readonly int _writeMs;
    private readonly Task _accept;
    private long _sent;
    private int _clients;

    /// <param name="writeMs">Time between writes: 100 (ten bursts a second) by default; 2 gives a steady stream, like a busy
    /// world, which is what a redraw cap is for.</param>
    public MudServer(int port, int bytesPerSecond, long limitBytes = long.MaxValue, bool chat = true, int writeMs = 100)
    {
        _bytesPerSecond = bytesPerSecond; _limitBytes = limitBytes; _chat = chat; _writeMs = Math.Max(1, writeMs);
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accept = AcceptAsync();
    }

    public int Port { get; }
    public long BytesSent => Interlocked.Read(ref _sent);
    public int Clients => Volatile.Read(ref _clients);

    private async Task AcceptAsync()
    {
        var seed = 1;
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            client.NoDelay = true;
            Interlocked.Increment(ref _clients);
            _ = ServeAsync(client, seed++);
        }
    }

    private async Task ServeAsync(TcpClient client, int seed)
    {
        using var owned = client;
        var stream = client.GetStream();
        var token = _stop.Token;
        // Drain whatever the client sends so its writes never block.
        _ = Task.Run(async () =>
        {
            var sink = new byte[4096];
            try { while (await stream.ReadAsync(sink, token) > 0) { } } catch { }
        }, token);
        var generator = new AnsiGenerator(seed, _chat);
        try
        {
            await Write(stream, "\u001b[1;33mWelcome to the bench world.\u001b[0m\r\nA loopback stand-in; nothing here is real.\r\n\r\n", token);
            long sentHere = 0;
            if (_bytesPerSecond <= 0)
            {
                while (!token.IsCancellationRequested)
                {
                    await Write(stream, "\u001b[32m<500hp 200m 300mv>\u001b[0m ", token);
                    await Task.Delay(2000, token);
                }
                return;
            }
            // One write per interval (ten a second by default), each worth what is due, paced against a clock so a slow
            // reader does not drift.
            var clock = Stopwatch.StartNew();
            var pending = new StringBuilder();
            while (!token.IsCancellationRequested && sentHere < _limitBytes)
            {
                var due = (long)(clock.Elapsed.TotalSeconds * _bytesPerSecond) - sentHere;
                if (due <= 0) { await Task.Delay(Math.Min(10, _writeMs), token); continue; }
                pending.Clear();
                while (pending.Length < due) pending.Append(generator.NextLine());
                var bytes = Encoding.UTF8.GetBytes(pending.ToString());
                await stream.WriteAsync(bytes, token);
                sentHere += bytes.Length;
                Interlocked.Add(ref _sent, bytes.Length);
                await Task.Delay(_writeMs, token);
            }
            while (!token.IsCancellationRequested) await Task.Delay(1000, token);
        }
        catch (Exception) { }
        finally { Interlocked.Decrement(ref _clients); }
    }

    private async Task Write(NetworkStream stream, string text, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes, token);
        Interlocked.Add(ref _sent, bytes.Length);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try { await _accept; } catch { }
    }
}
