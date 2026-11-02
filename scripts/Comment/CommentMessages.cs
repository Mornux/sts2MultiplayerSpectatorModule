using System.Text.Json;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 验证并转发评论消息。
// Validate and relay comment messages.

public partial class SpectatorSession
{
    public sealed record Comment(string Name, string Text, string Color);
    public event Action<Comment>? CommentReceived;
    private readonly Dictionary<ulong, double> _commentTimes = new();
    private double _lastComment = -10;
    private long _commentSequence, _lastCommentSequence;
    private ulong _commentHost;
    private string _commentToken = "";
    public bool CanSendComment => Watching && Map != null || !ReplayRunning && RunManager.Instance.IsInProgress
        && !RunManager.Instance.IsCleaningUp && RunManager.Instance.NetService.Type is NetGameType.Host or NetGameType.Client;
    public static bool ValidComment(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 100
        && text.All(ch => !char.IsControl(ch));

    // 房主统一校验身份和频率，再向所有参战玩家及观众转发。
    // Authenticate and rate-limit at the host, then relay to all participants and viewers.
    public bool SendComment(string text)
    {
        text = text.Trim();
        if (!CanSendComment || !ValidComment(text) || _time - _lastComment < 1) return false;
        _lastComment = _time;
        if (Watching) Send(_host, new(SpectatorWireProtocol.Version, SpectatorWireProtocol.CommentSendKind, _token, text));
        else if (IsHost) PublishComment(RunManager.Instance.NetService.NetId, text);
        else if (RunManager.Instance.NetService is NetClientGameService client)
            client.SendMessage(new SpectatorLocalRelay { Payload = JsonSerializer.Serialize(new SpectatorPacket(SpectatorWireProtocol.Version, SpectatorWireProtocol.CommentSendKind, _mapKey, text), WireJson) });
        return true;
    }

    private bool HandleComment(ulong sender, SpectatorPacket packet)
    {
        if (packet.Kind is not (SpectatorWireProtocol.CommentSendKind or SpectatorWireProtocol.CommentKind)) return false;
        if (packet.Kind == SpectatorWireProtocol.CommentSendKind)
        {
            if (AuthenticatedViewer(sender, packet.Token) || FromReadyPlayer(sender))
                PublishComment(sender, packet.Text);
        }
        else if (sender == _host && packet.Token == _token && Watching
            || !ReplayRunning && FromGameHost(sender))
        {
            if (_commentHost != sender || _commentToken != packet.Token) { _commentHost = sender; _commentToken = packet.Token; _lastCommentSequence = 0; }
            if (packet.Sequence <= _lastCommentSequence) return true;
            if (ValidComment(packet.Text) && ValidColor(packet.Color))
            {
                _lastCommentSequence = packet.Sequence;
                CommentReceived?.Invoke(new(CleanName(packet.Name), packet.Text, packet.Color));
            }
        }
        return true;
    }

    private void PublishComment(ulong sender, string text)
    {
        if (!ValidComment(text) || _commentTimes.TryGetValue(sender, out double last) && _time - last < 1) return;
        var player = RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault(player => player.NetId == sender);
        bool observer = _viewers.TryGetValue(sender, out var viewer);
        if (!observer && player == null) return;
        _commentTimes[sender] = _time;
        string name = observer ? viewer!.Name : CleanName(PlayerName(sender));
        string color = observer ? viewer!.Color : player!.Character.MapDrawingColor.ToHtml(false);
        var message = new Comment(name, text, color);
        CommentReceived?.Invoke(message);
        var packet = new SpectatorPacket(SpectatorWireProtocol.Version, SpectatorWireProtocol.CommentKind, _mapKey, text, ++_commentSequence, Name: name, Color: color);
        foreach (var peer in ((NetHostGameService)RunManager.Instance.NetService).ConnectedPeers.Where(peer => peer.readyForBroadcasting))
            if (peer.peerId != RunManager.Instance.NetService.NetId)
            {
                RunManager.Instance.NetService.SendMessage(new SpectatorLocalRelay { Payload = JsonSerializer.Serialize(packet, WireJson) }, peer.peerId);
                if (!IsLocalHost) Send(peer.peerId, packet);
            }
        BroadcastViewers(packet);
    }
}
