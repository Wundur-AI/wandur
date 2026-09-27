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

    private static byte[] WindowSize(int columns, int rows) => Sub(31, (byte)(columns >> 8), (byte)columns, (byte)(rows >> 8), (byte)rows);

    [Fact]
    public async Task TheSizeKnownBeforeNegotiationIsSentOnDoNaws()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (session, server) = await ConnectAsync();
        await using var _ = session;
        await session.UpdateWindowSizeAsync(132, 47, timeout.Token);
        server.Send(255, 253, 31);
        byte[] expected = [255, 251, 31, .. WindowSize(132, 47)];
        Assert.Equal(expected, await server.ReadWrittenAsync(expected.Length, timeout.Token));
    }

    [Fact]
    public async Task NoUpdateIsSentBeforeNawsIsAgreed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (session, server) = await ConnectAsync();
        await using var _ = session;
        await session.UpdateWindowSizeAsync(90, 30, timeout.Token);
        await session.UpdateWindowSizeAsync(91, 31, timeout.Token);
        Assert.Empty(server.Written);
        // The server refusing to ask keeps it that way.
        server.Send(255, 254, 31);
        await session.UpdateWindowSizeAsync(92, 32, timeout.Token);
        Assert.Empty(server.Written);
    }

    [Fact]
    public async Task AResizeAfterAgreementSendsAnUpdateOnce()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (session, server) = await ConnectAsync();
        await using var _ = session;
        server.Send(255, 253, 31);
        byte[] initial = [255, 251, 31, .. WindowSize(100, 40)];
        Assert.Equal(initial, await server.ReadWrittenAsync(initial.Length, timeout.Token));
        await session.UpdateWindowSizeAsync(80, 24, timeout.Token);
        Assert.Equal(WindowSize(80, 24), await server.ReadWrittenAsync(WindowSize(80, 24).Length, timeout.Token));
        // The same size again is not news.
        await session.UpdateWindowSizeAsync(80, 24, timeout.Token);
        Assert.Equal(0, server.Unread);
        // Sizes are kept inside what a server can use.
        await session.UpdateWindowSizeAsync(3, 9999, timeout.Token);
        Assert.Equal(WindowSize(20, 500), await server.ReadWrittenAsync(WindowSize(20, 500).Length, timeout.Token));
    }

    /// <summary>
    /// The receive loop feeds DO NAWS, which produces WILL NAWS and the current size, and a resize lands after
    /// Feed returned but before that reply was written. The update must still reach the server after the reply,
    /// or the server ends up with the stale size from the reply.
    /// </summary>
    [Fact]
    public async Task AResizeRacingANegotiationReplyIsWrittenAfterIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var profile = new ConnectionProfile { Host = "fake.test", Port = 4000 };
        var (session, server) = await ConnectAsync(profile);
        await using var _ = session;
        Task? resize = null;
        // Raised by the receive loop between Feed and writing its reply.
        session.ProtocolStateChanged += _ => resize ??= session.UpdateWindowSizeAsync(130, 60, timeout.Token);
        byte[] read = [255, 251, 69, 255, 253, 31];
        server.Send(read);
        var reply = new Wandur.Core.Protocol.TelnetParser(TelnetSession.ParserOptions(profile)).Feed(read).Reply;
        byte[] expected = [.. reply, .. WindowSize(130, 60)];
        Assert.Equal(expected, await server.ReadWrittenAsync(expected.Length, timeout.Token));
        await resize!.WaitAsync(timeout.Token);
        Assert.Equal(0, server.Unread);
    }

    [Fact]
    public async Task ResizesAndRepliesStayInParserOrderUnderLoad()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (session, server) = await ConnectAsync();
        await using var _ = session;
        server.Send(255, 253, 31);
        await server.ReadWrittenAsync(3 + WindowSize(100, 40).Length, timeout.Token);
        // Slow every write down so resizes and replies pile up behind one another.
        server.BeforeWrite = _ => Task.Delay(1);
        var resizes = Enumerable.Range(0, 40).Select(i => Task.Run(() => session.UpdateWindowSizeAsync(100 + i, 40, timeout.Token))).ToArray();
        // DONT NAWS then DO NAWS makes the parser resend the size it holds in its reply.
        for (var i = 0; i < 10; i++) server.Send(255, 254, 31, 255, 253, 31);
        await Task.WhenAll(resizes).WaitAsync(timeout.Token);
        await session.UpdateWindowSizeAsync(200, 70, timeout.Token);
        byte[] last = WindowSize(200, 70);
        while (true)
        {
            var written = server.Written;
            if (written.Length >= last.Length && written.AsSpan()[^last.Length..].SequenceEqual(last)) break;
            await Task.Delay(5, timeout.Token);
        }
    }

    /// <summary>Everything the session raised, in order: text as "t:", prompts as "p:" (with "!" when flagged private).</summary>
    private static List<string> Record(TelnetSession session)
    {
        var log = new List<string>();
        session.TextReceived += text => { lock (log) log.Add("t:" + text.Text + (text.MayContainPrivateText ? "!" : "")); };
        session.PromptReceived += prompt => { lock (log) log.Add("p:" + prompt.Text + (prompt.MayContainPrivateText ? "!" : "")); };
        return log;
    }

    private static async Task WaitForAsync(List<string> log, Func<List<string>, bool> done)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true) { lock (log) if (done(log)) return; await Task.Delay(5, timeout.Token); }
    }

    private static byte[] Ascii(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task GoAheadRaisesOnePromptWithTheLineAfterItsText()
    {
        var (session, server) = await ConnectAsync();
        await using var _ = session;
        var log = Record(session);
        server.Send([.. Ascii("Welcome\r\n\u001b[32mHP: 100>\u001b[0m "), 255, 249, .. Ascii("\r\nYou look around.\r\n")]);
        server.Send(Ascii("end\n"));
        await WaitForAsync(log, l => l.Count > 0 && l[^1].EndsWith("end\n"));
        lock (log) Assert.Equal(["t:Welcome\r\n\u001b[32mHP: 100>\u001b[0m ", "p:HP: 100> ", "t:\r\nYou look around.\r\n", "t:end\n"], log);
    }

    [Fact]
    public async Task EndOfRecordRaisesOnePromptAndGoAheadWithItDoesNotRaiseASecond()
    {
        var (session, server) = await ConnectAsync();
        await using var _ = session;
        var log = Record(session);
        server.Send([255, 251, 25, .. Ascii("Mana: 7> "), 255, 239]);
        server.Send([.. Ascii("Mana: 8> "), 255, 239, 255, 249]);
        server.Send(Ascii("done\n"));
        await WaitForAsync(log, l => l.Count > 0 && l[^1] == "t:done\n");
        lock (log) Assert.Equal(["t:Mana: 7> ", "p:Mana: 7> ", "t:Mana: 8> ", "p:Mana: 8> ", "t:done\n"], log);
    }

    [Fact]
    public async Task APromptSplitAcrossReadsIsRaisedOnceWhenItsMarkArrives()
    {
        var (session, server) = await ConnectAsync();
        await using var _ = session;
        var log = Record(session);
        server.Send(Ascii("HP: 1"));
        server.Send(Ascii("00> "));
        server.Send(255);
        server.Send(249);
        server.Send(Ascii("\n"));
        await WaitForAsync(log, l => l.Count > 0 && l[^1] == "t:\n");
        lock (log) Assert.Equal(["t:HP: 1", "t:00> ", "p:HP: 100> ", "t:\n"], log);
    }

    [Fact]
    public async Task AUtf8CharacterSplitAcrossReadsBeforeTheMarkIsDecodedWhole()
    {
        var (session, server) = await ConnectAsync();
        await using var _ = session;
        var log = Record(session);
        var euro = Encoding.UTF8.GetBytes("\u20ac");
        server.Send([.. Ascii("Gold "), euro[0], euro[1]]);
        server.Send([euro[2], .. Ascii("5> "), 255, 249, .. Ascii("\u00e9t\u00e9\n")]);
        await WaitForAsync(log, l => l.Count > 0 && l[^1].EndsWith("\n"));
        lock (log)
        {
            Assert.Equal("p:Gold \u20ac5> ", Assert.Single(log, e => e.StartsWith("p:")));
            Assert.Equal("Gold \u20ac5> \u00e9t\u00e9\n", string.Concat(log.Where(e => e.StartsWith("t:")).Select(e => e[2..])));
            Assert.DoesNotContain(log, e => e.Contains('\ufffd'));
        }
    }

    [Fact]
    public async Task PromptsDuringPrivateStretchesAreFlaggedPrivate()
    {
        var (session, server) = await ConnectAsync();
        await using var _ = session;
        var log = Record(session);
        // Server echo off: the password prompt and anything typed are private.
        server.Send([255, 251, 1, .. Ascii("Password: "), 255, 249]);
        await WaitForAsync(log, l => l.Any(e => e.StartsWith("p:")));
        server.Send([255, 252, 1, .. Ascii("\r\n")]);
        session.SetLocalPrivateInput(true);
        server.Send([.. Ascii("PIN: "), 255, 249]);
        await WaitForAsync(log, l => l.Count(e => e.StartsWith("p:")) == 2);
        session.SetLocalPrivateInput(false);
        server.Send([.. Ascii("\r\nHP: 9> "), 255, 249]);
        await WaitForAsync(log, l => l.Count(e => e.StartsWith("p:")) == 3);
        lock (log) Assert.Equal(["p:Password: !", "p:PIN: !", "p:HP: 9> "], log.Where(e => e.StartsWith("p:")));
    }

    [Fact]
    public async Task ABlankLineOrARepeatedMarkRaisesNoPrompt()
    {
        var (session, server) = await ConnectAsync();
        await using var _ = session;
        var log = Record(session);
        server.Send([.. Ascii("text\r\n"), 255, 249, 255, 249]);
        server.Send([255, 249]);
        server.Send(Ascii("barrier\n"));
        await WaitForAsync(log, l => l.Count > 0 && l[^1] == "t:barrier\n");
        lock (log) Assert.DoesNotContain(log, e => e.StartsWith("p:"));
    }
}
