using System.Text.Json;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 非确定性事件由玩家端补充实际页面，房主仍是唯一的观战数据来源。
// Participants supplement non-deterministic pages; only the host sends spectator state.
public partial class SpectatorSession
{
    internal event Action<ulong, string>? EventPeerReceived;
    internal event Action<string>? EventContextReceived;
    internal void SendEventContext(string json)
    {
        if (!IsHost || ReplayRunning || !HasViewer) return;
        foreach (var peer in ((NetHostGameService)RunManager.Instance.NetService).ConnectedPeers.Where(peer => peer.readyForBroadcasting))
            RunManager.Instance.NetService.SendMessage(new SpectatorLocalRelay
            { Payload = JsonSerializer.Serialize(new SpectatorPacket(SpectatorWireProtocol.Version, "event-context", _mapKey, json), WireJson) }, peer.peerId);
    }

    internal void SendEventPeer(string json, string context, long sequence)
    {
        if (!ReplayRunning && RunManager.Instance.NetService is NetClientGameService client && DisplayViewerId != 0)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            if (bytes.Length > Events.EventStateBounds.MaxPageBytes) return;
            foreach (var frame in BattleFrame.Split("event-local", context, sequence, (long)Godot.Time.GetTicksMsec(), bytes))
                client.SendMessage(new SpectatorLocalRelay { Payload = JsonSerializer.Serialize(
                    new SpectatorPacket(SpectatorWireProtocol.Version, "event-local", _mapKey, JsonSerializer.Serialize(frame, WireJson)), WireJson) });
        }
    }
    private bool HandleEventPeer(ulong sender, SpectatorPacket packet)
    {
        if (packet.Kind == "event-context")
        {
            if (FromGameHost(sender)) EventContextReceived?.Invoke(packet.Text);
            return true;
        }
        if (packet.Kind != "event-local") return false;
        if (FromReadyPlayer(sender)) EventPeerReceived?.Invoke(sender, packet.Text);
        return true;
    }
}
