using System.Text;
using Wandur.Core.Sessions;
using Wandur.Core.Settings;

namespace Wandur.Core.Tests;

/// <summary>What a session tells the server about the client, over an in-memory connection.</summary>
public sealed class TelnetSessionProtocolTests
{
    private static async Task<(TelnetSession Session, FakeServerStream Server)> ConnectAsync(ConnectionProfile? profile = null)
    {
        var server = new FakeServerStream();
        var session = new TelnetSession(profile ?? new() { Host = "fake.test", Port = 4000 }, _ => Task.FromResult<Stream>(server));
        await session.ConnectAsync();
        return (session, server);
    }

    private static byte[] Sub(byte option, params byte[] payload) => [255, 250, option, .. payload, 255, 240];
    private static byte[] TerminalType(string name) => Sub(24, [0, .. Encoding.ASCII.GetBytes(name)]);

    private static async Task<string[]> TerminalTypesAsync(ConnectionProfile profile)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (session, server) = await ConnectAsync(profile);
        await using var _ = session;
        server.Send(255, 253, 24);
        Assert.Equal(new byte[] { 255, 251, 24 }, await server.ReadWrittenAsync(3, timeout.Token));
        var answers = new List<string>();
        foreach (var expectedLength in new[] { "Wandur-WMC", "XTERM-256COLOR" })
        {
            server.Send(Sub(24, 1));
            var reply = await server.ReadWrittenAsync(TerminalType(expectedLength).Length, timeout.Token);
            answers.Add(Encoding.ASCII.GetString(reply[4..^2]));
        }
        server.Send(Sub(24, 1));
        // "MTTS " plus three or four digits: read the frame up to IAC SE.
        var frame = new List<byte>();
        while (frame.Count < 2 || frame[^2] != 255 || frame[^1] != 240) frame.AddRange(await server.ReadWrittenAsync(1, timeout.Token));
        answers.Add(Encoding.ASCII.GetString(frame.ToArray()[4..^2]));
        return [.. answers];
    }

    [Fact]
    public async Task AUtf8ProfileAdvertisesUtf8InTheMttsCycle()
    {
        var answers = await TerminalTypesAsync(new() { Host = "fake.test", Port = 4000, Encoding = "utf-8" });
        // ANSI 1 + VT100 2 + UTF-8 4 + 256 colors 8 + truecolor 256.
        Assert.Equal(["Wandur-WMC", "XTERM-256COLOR", "MTTS 271"], answers);
    }

    [Fact]
    public async Task ALatin1ProfileNeverClaimsUtf8()
    {
        var answers = await TerminalTypesAsync(new() { Host = "fake.test", Port = 4000, Encoding = "latin1" });
        Assert.Equal(["Wandur-WMC", "XTERM-256COLOR", "MTTS 267"], answers);
    }

    [Fact]
    public async Task ATlsProfileAddsTheSslBit()
    {
        var answers = await TerminalTypesAsync(new() { Host = "fake.test", Port = 4000, Encoding = "utf-8", UseTls = true });
        Assert.Equal(["Wandur-WMC", "XTERM-256COLOR", "MTTS 2319"], answers);
    }

    [Fact]
    public async Task EndOfRecordAndMsspAreAccepted()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (session, server) = await ConnectAsync();
        await using var _ = session;
        server.Send(255, 251, 25);
        Assert.Equal(new byte[] { 255, 253, 25 }, await server.ReadWrittenAsync(3, timeout.Token));
        server.Send(255, 251, 70);
        Assert.Equal(new byte[] { 255, 253, 70 }, await server.ReadWrittenAsync(3, timeout.Token));
    }
}
