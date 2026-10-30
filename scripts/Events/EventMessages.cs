using System.Text.Json;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 非确定性事件由玩家端补充实际页面，房主仍是唯一的观战数据来源。
// Participants supplement non-deterministic pages; only the host sends spectator state.
public partial class SpectatorSession
{
    internal event Action<ulong, string>? EventPeerReceived;
    internal void SendEventPeer(string json)
    {
        if (!ReplayRunning && RunManager.Instance.NetService is NetClientGameService client && DisplayViewerId != 0)
        {
            string payload = JsonSerializer.Serialize(new SpectatorPacket(2, "event-local", _mapKey, json), WireJson);
            if (System.Text.Encoding.UTF8.GetByteCount(payload) <= SpectatorWireProtocol.MaxBytes)
                client.SendMessage(new SpectatorLocalRelay { Payload = payload });
        }
    }
    private bool HandleEventPeer(ulong sender, SpectatorPacket packet)
    {
        if (packet.Kind != "event-local") return false;
        if (FromReadyPlayer(sender)) EventPeerReceived?.Invoke(sender, packet.Text);
        return true;
    }
}
