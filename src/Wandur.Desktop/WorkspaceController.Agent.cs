using System.Text;
using System.Text.Json;
using Wandur.Core.Mapping;
using Wandur.Core.Agents;
using Wandur.Core.Terminal;
using Wandur.Desktop.Services;
using Wandur.Desktop.ViewModels;

namespace Wandur.Desktop;

public sealed partial class WorkspaceController
{
    private AgentRunner? _agentRunner;
    private bool _agentOwnsControl;
    private readonly AnsiTerminal _agentTranscript = new(60);
    private string _agentPublicText = "";
    /// <summary>Output arrived while no agent run was going; the filtered text is rebuilt when one observes.</summary>
    private bool _agentTextStale;
    private string _agentProtocol = "";
    private long _agentRevision;
    private long _agentGeneration;
    public AgentSessionViewModel? Agent { get; private set; }

    private void InitializeAgent(IAgentClientServices? services)
    {
        if (services is null) return;
        _agentRunner = new(services.Providers, new AgentGateway(this), services.ReadCredentialAsync);
        Agent = new(_agentRunner, services);
    }
    private void ResetAgentContext(string status = "AgentPaused")
    {
        _agentRunner?.Stop(status); _agentGeneration++;
        _agentTranscript.Clear(); _agentPublicText = ""; _agentTextStale = false; _agentProtocol = ""; _agentRevision++;
    }
    private void FeedAgentText(string text)
    {
        if (_agentRunner is null) return;
        _agentTranscript.Append(text);
        // Rebuilding means every kept line through the channel rules, so it is done per chunk only while a run is
        // going (its revision checks need it current); otherwise once, when the next run first observes.
        if (_agentRunner.IsBusy) RebuildAgentText();
        else _agentTextStale = true;
    }
    private void RebuildAgentText()
    {
        _agentTextStale = false;
        var plain = _agentTranscript.PlainText;
        // Chat is not useful evidence that a movement or command completed.
        plain = string.Join('\n', plain.Split('\n').Where(line => !IsChat(line)));
        if (plain.Length > 8000) plain = plain[^8000..];
        if (plain == _agentPublicText) return;
        _agentPublicText = plain; _agentRevision++;
    }
    /// <summary>The panel and the agent filter agree on what chat is because both ask the classifier.</summary>
    private bool IsChat(string line) => _channels.IsChannelLine(line);
    private void FeedAgentProtocol(byte option, byte[]? payload)
    {
        if (_agentRunner is null || payload is null) return;
        // Only room/vitals messages, never login/account packages or generic protocol dumps.
        string text;
        if (option == 69)
        {
            var room = RoomProtocolDecoder.FromMsdp(payload);
            if (room is null) return;
            text = JsonSerializer.Serialize(room);
        }
        else
        {
            text = Encoding.UTF8.GetString(payload);
            if (option != 201 || !(text.StartsWith("Room.Info ", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Char.Vitals ", StringComparison.OrdinalIgnoreCase))) return;
        }
        text = text.Length > 4000 ? text[..4000] : text;
        if (text == _agentProtocol) return;
        _agentProtocol = text; _agentRevision++;
    }
    private sealed class AgentGateway(WorkspaceController owner) : IAgentSessionGateway
    {
        public AgentObservation Observe()
        {
            owner.FlushOutput();
            if (owner._agentTextStale) owner.RebuildAgentText();
            return new(owner._agentRevision, owner._agentGeneration,
                owner._agentProtocol + "\nRecent world output:\n" + owner._agentPublicText,
                owner.IsConnected && !owner.IsConnecting && !owner._disposed && !owner.IsPrivate && owner._login is null && owner._session is not Wandur.Core.Sessions.TelnetSession { RemoteEcho: true });
        }
        public void SetAgentControl(bool enabled)
        {
            owner._agentOwnsControl = enabled;
            if (enabled) owner.StopMapWalk();
            owner.ScriptLibrary.SetSuspended(enabled);
        }
        public Task<bool> SendAsync(string command, AgentObservation expected, CancellationToken cancellationToken)
        {
            var now = Observe();
            if (cancellationToken.IsCancellationRequested || !now.CanAct || now.Generation != expected.Generation || now.Revision != expected.Revision)
                return Task.FromResult(false);
            return owner.SendCoreAsync(command, fromAgent: true, cancellationToken: cancellationToken, agentObservation: expected);
        }
    }
}
