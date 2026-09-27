using L = Wandur.Core.Localization.Strings;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Wandur.Core.Protocol;
using Wandur.Core.Mapping;
using Wandur.Core.Settings;

namespace Wandur.Core.Sessions;

public sealed class TelnetSession : IMudSession
{
    private readonly ConnectionProfile profile;
    private readonly Func<CancellationToken, Task<Stream>>? _open;
    private readonly TelnetParser _parser;
    private readonly object _parserLock = new();
    /// <summary>Negotiation bytes in the order the parser produced them: Feed replies and NAWS updates. Enqueued
    /// under <see cref="_parserLock"/>, drained in order under <see cref="_sendLock"/>, so a resize can never
    /// reach the server ahead of a reply the parser produced before it.</summary>
    private readonly Queue<byte[]> _negotiation = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly TcpClient _client = new() { NoDelay = true };
    private CancellationTokenSource? _lifetime;
    private Stream? _stream;
    private Task? _receiveTask;
    private int _started;
    private int _disposed;
    private volatile bool _connected;
    public event Action<string>? Output;
    public event Action<ReceivedSessionText>? TextReceived;
    public event Action<SessionStatus>? StatusChanged;
    public event Action<bool>? PrivateInputChanged;
    public event Action<string>? GmcpReceived;
    public event Action<GmcpLoginMessage>? GmcpLoginReceived;
    public event Action<ReceivedSessionText>? GmcpMessageReceived;
    public event Action<TelnetDataMessage>? ProtocolMessageReceived;
    public event Action<RoomObservation>? RoomReceived;
    public event Action<TelnetProtocolState>? ProtocolStateChanged;
    /// <summary>The server described itself through MSSP (telnet option 70). Raised on the receive thread.</summary>
    public event Action<MsspTable>? MsspReceived;
    /// <summary>The newest MSSP table the server sent on this connection, or null when it sent none.</summary>
    public MsspTable? Mssp { get; private set; }
    /// <summary>Raised before room metadata even when ECHO enters and leaves within one read.</summary>
    public event Action? PrivateIntervalReceived;
    private volatile bool _remoteEcho;
    public bool RemoteEcho => _remoteEcho;
    public TelnetProtocolState ProtocolState { get; private set; } = new();
    public bool IsConnected => _connected;
    private volatile bool _localPrivate;
    private long _privacyEpoch;
    public void SetLocalPrivateInput(bool enabled)
    {
        lock (_parserLock)
        {
            if (_localPrivate != enabled) Interlocked.Increment(ref _privacyEpoch);
            _localPrivate = enabled;
            _parser.SetLocalPrivateInput(enabled);
        }
    }
    private Encoding TextEncoding => profile.Encoding == "latin1" ? Encoding.Latin1 : Encoding.UTF8;

    public TelnetSession(ConnectionProfile profile) : this(profile, null) { }

    /// <param name="open">Replaces the TCP and TLS connection with a stream of the caller's; for tests.</param>
    internal TelnetSession(ConnectionProfile profile, Func<CancellationToken, Task<Stream>>? open)
    {
        this.profile = profile;
        _open = open;
        _parser = new(ParserOptions(profile));
    }

    /// <summary>
    /// What this connection tells the server: the Wandur name and an xterm terminal type through TTYPE, then the
    /// MTTS capabilities, which claim UTF-8 only when the profile decodes UTF-8 and add the TLS bit on TLS. EOR
    /// is accepted so servers mark prompts, and MSSP so a server can describe itself.
    /// </summary>
    internal static TelnetParserOptions ParserOptions(ConnectionProfile profile)
    {
        var capabilities = MttsCapabilities.Ansi | MttsCapabilities.Vt100 | MttsCapabilities.Colors256 | MttsCapabilities.TrueColor;
        if (profile.Encoding != "latin1") capabilities |= MttsCapabilities.Utf8;
        if (profile.UseTls) capabilities |= MttsCapabilities.Ssl;
        return new()
        {
            ClientName = ClientIdentity.TerminalType,
            TerminalType = "XTERM-256COLOR",
            Capabilities = capabilities,
            AcceptEndOfRecord = true,
            AcceptMssp = true
        };
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        profile.Validate();
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException(L.CreateANewSessionToReconnect);
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            if (_open is not null) _stream = await _open(timeout.Token).ConfigureAwait(false);
            else
            {
                await _client.ConnectAsync(profile.Host, profile.Port, timeout.Token).ConfigureAwait(false);
                _stream = _client.GetStream();
            }
            if (profile.UseTls && _open is null)
            {
                var tls = new SslStream(_stream, leaveInnerStreamOpen: false);
                _stream = tls;
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = profile.Host }, timeout.Token).ConfigureAwait(false);
            }
            _connected = true;
            StatusChanged?.Invoke(new(true, profile.UseTls ? L.ConnectedTLS : L.ConnectedTelnet));
            _receiveTask = ReceiveAsync(_lifetime.Token);
        }
        catch
        {
            _connected = false; _stream?.Dispose(); _client.Dispose();
            throw;
        }
    }

    private async Task ReceiveAsync(CancellationToken token)
    {
        var bytes = new byte[8192];
        _characters = new char[TextEncoding.GetMaxCharCount(bytes.Length)];
        _decoder = TextEncoding.GetDecoder();
        string status = L.ServerClosedTheConnection;
        try
        {
            while (!token.IsCancellationRequested)
            {
                int count = await _stream!.ReadAsync(bytes, token).ConfigureAwait(false);
                if (count == 0) break;
                bool wasPrivate = _parser.ServerEcho;
                TelnetPacket packet;
                lock (_parserLock)
                {
                    packet = _parser.Feed(bytes.AsSpan(0, count));
                    if (packet.Reply.Length > 0) _negotiation.Enqueue(packet.Reply);
                }
                _remoteEcho = _parser.ServerEcho;
                if (packet.MayContainPrivateText)
                {
                    Interlocked.Increment(ref _privacyEpoch);
                    PrivateIntervalReceived?.Invoke();
                }
                foreach (var table in packet.Mssp)
                {
                    Mssp = table;
                    MsspReceived?.Invoke(table);
                }
                if (ProtocolState != _parser.ProtocolState)
                {
                    ProtocolState = _parser.ProtocolState;
                    ProtocolStateChanged?.Invoke(ProtocolState);
                }
                if (packet.Reply.Length > 0) await FlushNegotiationAsync(token).ConfigureAwait(false);
                if (wasPrivate != _parser.ServerEcho) PrivateInputChanged?.Invoke(_parser.ServerEcho);
                // Servers may negotiate both protocols. Keep updates in wire order so an older
                // room from one protocol cannot overwrite the newest room from the other.
                foreach (var message in packet.DataMessages)
                {
                    ProtocolMessageReceived?.Invoke(message with { MayContainPrivateText = message.MayContainPrivateText || packet.MayContainPrivateText });
                    RoomObservation? room;
                    if (message.Option == 201)
                    {
                        var gmcp = Encoding.UTF8.GetString(message.Payload);
                        if (GmcpLoginProtocol.Decode(gmcp) is { } login) GmcpLoginReceived?.Invoke(login);
                        GmcpReceived?.Invoke(gmcp);
                        GmcpMessageReceived?.Invoke(new(gmcp, message.MayContainPrivateText || packet.MayContainPrivateText));
                        room = RoomProtocolDecoder.FromGmcp(gmcp);
                    }
                    else room = RoomProtocolDecoder.FromMsdp(message.Payload);
                    if (room is not null) RoomReceived?.Invoke(room);
                }
                // A prompt mark splits the text: what came before it is decoded and delivered first, then the
                // prompt is raised, and the rest continues with the same decoder, so a character split across
                // reads or around the mark completes instead of becoming U+FFFD.
                var start = 0;
                foreach (var mark in packet.PromptMarks)
                {
                    DeliverText(packet.Text, start, mark.Offset - start, packet.MayContainPrivateText);
                    start = mark.Offset;
                    RaisePrompt(packet.MayContainPrivateText);
                }
                DeliverText(packet.Text, start, packet.Text.Length - start, packet.MayContainPrivateText);
            }
        }
        catch (OperationCanceledException) { status = L.Disconnected; }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        { status = token.IsCancellationRequested ? L.Disconnected : L.Format(L.ConnectionLost, ex.Message); }
        finally
        {
            _connected = false;
            _stream?.Dispose(); _client.Dispose();
            PrivateInputChanged?.Invoke(false);
            StatusChanged?.Invoke(new(false, status));
        }
    }

    private Decoder _decoder = Encoding.UTF8.GetDecoder();
    private char[] _characters = [];
    private bool _decoderMayContainPrivateText;
    /// <summary>The line in progress, for the prompt a mark ends; two lines are enough to hold the current one.</summary>
    private readonly Wandur.Core.Terminal.AnsiTerminal _promptLine = new(2);
    private bool _promptLinePrivate;
    private bool _textSinceMark;

    /// <summary>
    /// A server prompt: the text since the last newline or the previous mark when the server sent IAC GA or IAC EOR,
    /// with ANSI styling removed.
    /// Raised on the receive thread after the text before the mark and before the text after it. A mark with
    /// no new text since the previous one (GA and EOR together, or GA after every write) raises nothing, and
    /// neither does a blank line. The flag is set when any part of the line may contain private text.
    /// </summary>
    public event Action<ReceivedSessionText>? PromptReceived;

    private void DeliverText(byte[] text, int offset, int count, bool packetPrivate)
    {
        var length = _decoder.GetChars(text, offset, count, _characters, 0, false);
        // Negotiation-only reads add no bytes to the decoder's pending character.
        if (count > 0) _decoderMayContainPrivateText |= packetPrivate;
        if (length == 0) return;
        var decoded = new string(_characters, 0, length);
        var mayContainPrivateText = _decoderMayContainPrivateText;
        TextReceived?.Invoke(new(decoded, mayContainPrivateText));
        Output?.Invoke(decoded);
        _promptLine.Append(decoded);
        // The line in progress is private when any part of it since its last newline was.
        _promptLinePrivate = decoded.Contains('\n') ? mayContainPrivateText : _promptLinePrivate || mayContainPrivateText;
        _textSinceMark = true;
        // Retain provenance only for an incomplete character at this segment's
        // end. Complete private text must not suppress the next public read.
        // Reads that emit no characters retain provenance until completion.
        _decoderMayContainPrivateText = packetPrivate &&
            TextEncoding.CodePage == Encoding.UTF8.CodePage && HasIncompleteUtf8Suffix(text.AsSpan(offset, count));
    }

    private void RaisePrompt(bool packetPrivate)
    {
        if (!_textSinceMark) return;
        _textSinceMark = false;
        var line = _promptLine.Lines[^1].Text;
        var mayContainPrivateText = _promptLinePrivate || packetPrivate;
        // A mark ends the prompt: the next one starts after it even when the server sends no newline between.
        _promptLine.Clear();
        _promptLinePrivate = false;
        if (string.IsNullOrWhiteSpace(line)) return;
        PromptReceived?.Invoke(new(line, mayContainPrivateText));
    }

    private static bool HasIncompleteUtf8Suffix(ReadOnlySpan<byte> bytes)
    {
        var lead = bytes.Length - 1;
        while (lead >= 0 && bytes[lead] is >= 0x80 and <= 0xBF) lead--;
        if (lead < 0) return false;
        var expected = bytes[lead] switch
        {
            >= 0xC2 and <= 0xDF => 2,
            >= 0xE0 and <= 0xEF => 3,
            >= 0xF0 and <= 0xF4 => 4,
            _ => 1
        };
        return bytes.Length - lead < expected;
    }

    public async Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (!_connected) throw new InvalidOperationException(L.ConnectToAWorldFirst);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime!.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await WriteAsync(TelnetParser.EncodeCommand(command, TextEncoding), timeout.Token).ConfigureAwait(false);
    }

    public async Task SendLoginCredentialsAsync(string? account, string? password, CancellationToken cancellationToken = default)
    {
        if (!_connected || ProtocolState.Gmcp != TelnetOptionState.Enabled) throw new InvalidOperationException(L.ConnectToAWorldFirst);
        // This path never publishes outgoing credentials to diagnostics, scripts or command history.
        var body = string.IsNullOrEmpty(account) || string.IsNullOrEmpty(password) ? "{}" :
            JsonSerializer.Serialize(new { account, password });
        var payload = Encoding.UTF8.GetBytes("Char.Login.Credentials " + body);
        if (payload.Length > ProtocolDiagnosticFormatter.MaximumPayloadBytes) throw new ArgumentException(L.CommandIsTooLong);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime!.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await WriteAsync(TelnetParser.Subnegotiation(201, payload), timeout.Token).ConfigureAwait(false);
    }

    /// <summary>Request fresh observations using fixed capabilities and validated mapped MSDP names.</summary>
    public async Task<bool> RefreshProtocolSubscriptionsAsync(Wandur.Models.WorldMapping? mapping = null, CancellationToken cancellationToken = default)
    {
        if (!_connected || _localPrivate || RemoteEcho ||
            ProtocolState is not { Gmcp: TelnetOptionState.Enabled } and not { Msdp: TelnetOptionState.Enabled }) return false;
        if (mapping is not null && (!Wandur.Models.MappingValidation.IsValid(mapping) ||
            !mapping.Endpoint.Matches(new(profile.Host, profile.Port, profile.UseTls)))) return false;
        var epoch = Interlocked.Read(ref _privacyEpoch);
        var requests = new List<(byte Option, byte[] Bytes)>();
        if (ProtocolState.Gmcp == TelnetOptionState.Enabled)
            requests.Add((201, TelnetParser.Subnegotiation(201, Encoding.UTF8.GetBytes(ProtocolDiscovery.GmcpSupports))));
        foreach (var option in new byte[] { 69, 201 })
            if (mapping is not null) AddMsdpRequests(requests, option, MappedMsdpNames(mapping, option == 69 ? "MSDP" : "GMCP"));
        return await SendRequestsAsync(requests, epoch, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asks the world to report, and send once, MSDP variables that scripts read but no mapping
    /// binds. The same REPORT and SEND requests as the mapped refresh, under the same guards.</summary>
    public async Task<bool> ReportMsdpAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken = default)
    {
        if (!_connected || _localPrivate || RemoteEcho || ProtocolState.Msdp != TelnetOptionState.Enabled) return false;
        var epoch = Interlocked.Read(ref _privacyEpoch);
        var requests = new List<(byte Option, byte[] Bytes)>();
        AddMsdpRequests(requests, 69, names.Where(IsValidMsdpName).Distinct(StringComparer.Ordinal).Take(256));
        return requests.Count == 0 || await SendRequestsAsync(requests, epoch, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A name the REPORT request accepts: letters, digits and underscore, not starting with a digit.</summary>
    public static bool IsValidMsdpName(string name)
        => name.Length is > 0 and <= 128 && !char.IsAsciiDigit(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <summary>The MSDP variables a mapping already asks the world to report, so a script request can skip them.</summary>
    public static IReadOnlyCollection<string> MappedMsdpNames(Wandur.Models.WorldMapping? mapping)
        => mapping is null ? [] : MappedMsdpNames(mapping, "MSDP").ToArray();

    private static IEnumerable<string> MappedMsdpNames(Wandur.Models.WorldMapping mapping, string protocol)
        => mapping.Bindings.Where(b => b.Source.Protocol == protocol && b.Source.Package == "MSDP")
            .Select(b => b.Source.Path.Split('/').ElementAtOrDefault(1) ?? "")
            .Where(IsValidMsdpName)
            .Distinct(StringComparer.Ordinal).Take(256);

    private static void AddMsdpRequests(List<(byte Option, byte[] Bytes)> requests, byte option, IEnumerable<string> names)
    {
        foreach (var batch in names.Chunk(32))
        foreach (var command in new[] { "REPORT", "SEND" })
        {
            var payload = option == 69 ? "\u0001" + command + string.Concat(batch.Select(name => "\u0002" + name)) :
                "MSDP " + JsonSerializer.Serialize(new Dictionary<string, string[]> { [command] = batch });
            requests.Add((option, TelnetParser.Subnegotiation(option, Encoding.UTF8.GetBytes(payload))));
        }
    }

    private async Task<bool> SendRequestsAsync(List<(byte Option, byte[] Bytes)> requests, long epoch, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime!.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await _sendLock.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            foreach (var request in requests)
            {
                if (!_connected || _localPrivate || RemoteEcho || epoch != Interlocked.Read(ref _privacyEpoch)) return false;
                if ((request.Option == 69 ? ProtocolState.Msdp : ProtocolState.Gmcp) != TelnetOptionState.Enabled) continue;
                await _stream!.WriteAsync(request.Bytes, timeout.Token).ConfigureAwait(false);
            }
            return true;
        }
        finally { _sendLock.Release(); }
    }

    /// <summary>The smallest and largest window a server is told about; a pane squeezed to a sliver is not a terminal.</summary>
    public const int MinimumColumns = 20, MinimumRows = 5, MaximumWindowSize = 500;

    /// <summary>
    /// Records the terminal's size for NAWS. Before the server asks with DO NAWS nothing is sent and the size is
    /// kept for the reply; once agreed, a changed size is sent at once. Callers debounce; this sends every change.
    /// The update goes through the same ordered queue as the parser's replies.
    /// </summary>
    public async Task UpdateWindowSizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        columns = Math.Clamp(columns, MinimumColumns, MaximumWindowSize);
        rows = Math.Clamp(rows, MinimumRows, MaximumWindowSize);
        lock (_parserLock)
        {
            var update = _parser.UpdateWindowSize(columns, rows);
            if (update.Length > 0) _negotiation.Enqueue(update);
        }
        if (!_connected || _lifetime is null) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await FlushNegotiationAsync(timeout.Token).ConfigureAwait(false);
    }

    /// <summary>Writes every queued negotiation in order. Whoever holds the send lock drains the whole queue, so
    /// bytes enqueued earlier are always written before bytes enqueued later.</summary>
    private async Task FlushNegotiationAsync(CancellationToken token)
    {
        await _sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            while (true)
            {
                byte[]? next;
                lock (_parserLock) if (!_negotiation.TryDequeue(out next)) return;
                await _stream!.WriteAsync(next, token).ConfigureAwait(false);
            }
        }
        finally { _sendLock.Release(); }
    }

    private async Task WriteAsync(byte[] bytes, CancellationToken token)
    {
        await _sendLock.WaitAsync(token).ConfigureAwait(false);
        try { await _stream!.WriteAsync(bytes, token).ConfigureAwait(false); }
        finally { _sendLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _connected = false;
        if (_lifetime is not null) await _lifetime.CancelAsync().ConfigureAwait(false);
        _client.Dispose();
        if (_receiveTask is not null) await _receiveTask.ConfigureAwait(false);
        _stream?.Dispose();
        _lifetime?.Dispose();
    }
}
