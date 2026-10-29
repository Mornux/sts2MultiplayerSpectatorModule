using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// Session-level routing. Each transport owns its own resources and handler lifecycle.
public partial class SpectatorSession
{
    private SteamSpectatorTransport? _steam;
    private NativeGameRelay? _relay;
    private SteamSpectatorTransport Steam => _steam ??= new(IsPeer, ReceivePacket);

    private void Send(ulong peer, SpectatorPacket packet)
    {
        byte[] bytes = SpectatorWireProtocol.Encode(packet);
        if (bytes.Length > SpectatorWireProtocol.MaxBytes) return;
        if (SpectatorLocal.IsLocal(peer)) _local?.Send(peer, bytes);
        else Steam.Send(peer, bytes);
    }

    // 所有大型快照使用同一个发送入口。
    // Send all large snapshots through one fragmentation path.
    internal void SendFragments(string kind, string key, long sequence, byte[] bytes)
    {
        foreach (var frame in BattleFrame.Split(kind, key, sequence, (long)Time.GetTicksMsec(), bytes))
            SendBattle(JsonSerializer.Serialize(frame, WireJson));
    }

    private void UpdateRelayService()
    {
        var service = !ReplayRunning && RunManager.Instance.IsInProgress
            && RunManager.Instance.NetService.Type is NetGameType.Host or NetGameType.Client
            ? RunManager.Instance.NetService : null;
        (_relay ??= new(ReceivePacket)).Update(service);
    }

    private void ReceivePacket(ulong sender, byte[] bytes)
    {
        try
        {
            if (SpectatorWireProtocol.Decode(bytes) is { } packet) Handle(sender, packet);
        }
        catch (Exception exception) { GD.PushWarning($"Local spectator packet ignored: {exception.Message}"); }
    }
}
