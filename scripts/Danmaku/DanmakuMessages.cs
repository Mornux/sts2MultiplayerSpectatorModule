using System.Text.Json;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 验证并转发弹幕消息。
// Validate and relay danmaku messages.

public partial class SpectatorSession
{
    public sealed record Danmaku(string Name, string Text, string Color);
    public event Action<Danmaku>? DanmakuReceived;
    private readonly Dictionary<ulong, double> _danmakuTimes = new();
    private double _lastDanmaku = -10;
    private long _chatSequence, _lastChatSequence;
    private ulong _chatHost;
    private string _chatToken = "";
    public bool CanSendDanmaku => Watching && Map != null || !ReplayRunning && RunManager.Instance.IsInProgress
        && !RunManager.Instance.IsCleaningUp && RunManager.Instance.NetService.Type is NetGameType.Host or NetGameType.Client;
    public static bool ValidDanmaku(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 100
        && text.All(ch => !char.IsControl(ch));

    // 房主统一校验身份和频率，再向所有参战玩家及观众转发。
    // Authenticate and rate-limit at the host, then relay to all participants and viewers.
    public bool SendDanmaku(string text)
    {
        text = text.Trim();
        if (!CanSendDanmaku || !ValidDanmaku(text) || _time - _lastDanmaku < 1) return false;
        _lastDanmaku = _time;
        if (Watching) Send(_host, new(2, "chat-send", _token, text));
        else if (IsHost) PublishDanmaku(RunManager.Instance.NetService.NetId, text);
        else if (RunManager.Instance.NetService is NetClientGameService client)
            client.SendMessage(new SpectatorLocalRelay { Payload = JsonSerializer.Serialize(new SpectatorPacket(2, "chat-send", _mapKey, text), WireJson) });
        return true;
    }

    private bool HandleDanmaku(ulong sender, SpectatorPacket packet)
    {
        if (packet.Kind is not ("chat-send" or "chat")) return false;
        if (packet.Kind == "chat-send")
        {
            if (IsHost && (sender == _viewer && packet.Token == _viewerToken
                || ((NetHostGameService)RunManager.Instance.NetService).ConnectedPeers.Any(peer => peer.peerId == sender && peer.readyForBroadcasting)))
                PublishDanmaku(sender, packet.Text);
        }
        else if (sender == _host && packet.Token == _token && Watching
            || !ReplayRunning && RunManager.Instance.IsInProgress && RunManager.Instance.NetService is NetClientGameService client
                && client.IsConnected && sender == client.HostNetId)
        {
            if (_chatHost != sender || _chatToken != packet.Token) { _chatHost = sender; _chatToken = packet.Token; _lastChatSequence = 0; }
            if (packet.Sequence > 0 && packet.Sequence <= _lastChatSequence) return true;
            if (ValidDanmaku(packet.Text) && ValidColor(packet.Color))
            {
                _lastChatSequence = packet.Sequence;
                DanmakuReceived?.Invoke(new(CleanName(packet.Name), packet.Text, packet.Color));
            }
        }
        return true;
    }

    private void PublishDanmaku(ulong sender, string text)
    {
        if (!ValidDanmaku(text) || _danmakuTimes.TryGetValue(sender, out double last) && _time - last < 1) return;
        var player = RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault(player => player.NetId == sender);
        bool observer = _viewer != 0 && sender == _viewer;
        if (!observer && player == null) return;
        _danmakuTimes[sender] = _time;
        string name = observer ? _viewerName : CleanName(PlayerName(sender));
        string color = observer ? _viewerColor : player!.Character.MapDrawingColor.ToHtml(false);
        var message = new Danmaku(name, text, color);
        DanmakuReceived?.Invoke(message);
        var packet = new SpectatorPacket(2, "chat", _mapKey, text, ++_chatSequence, Name: name, Color: color);
        foreach (var peer in ((NetHostGameService)RunManager.Instance.NetService).ConnectedPeers.Where(peer => peer.readyForBroadcasting))
            if (peer.peerId != RunManager.Instance.NetService.NetId)
            {
                RunManager.Instance.NetService.SendMessage(new SpectatorLocalRelay { Payload = JsonSerializer.Serialize(packet, WireJson) }, peer.peerId);
                if (!IsLocalHost) Send(peer.peerId, packet);
            }
        if (_viewer != 0) Send(_viewer, packet with { Token = _viewerToken });
    }
}
