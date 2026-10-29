using System.Text;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 通过原版联机连接转发消息。
// Relay messages through native multiplayer connections.

// 原版联机客户端通过自己的游戏连接接收观众名单和画线。
// Regular clients receive spectator rosters and ink through their existing game connection.
public struct SpectatorLocalRelay : INetMessage
{
    public string Payload;
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public void Serialize(PacketWriter writer)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Payload);
        writer.WriteInt(bytes.Length);
        writer.WriteBytes(bytes, bytes.Length);
    }
    public void Deserialize(PacketReader reader)
    {
        int length = reader.ReadInt();
        if (length is < 1 or > SpectatorWireProtocol.MaxBytes) throw new InvalidOperationException("Invalid spectator relay length.");
        var bytes = new byte[length];
        reader.ReadBytes(bytes, length);
        Payload = Encoding.UTF8.GetString(bytes);
    }
}

// 跟随原版实际开房端口，默认游戏 33771、观战 33772。
// Follow the native host port: game 33771 and spectator 33772 by default.
[HarmonyPatch(typeof(NetHostGameService), nameof(NetHostGameService.StartENetHost))]
internal static class SpectatorLocalHostPort
{
    private static void Postfix(ushort port) => SpectatorLocal.GamePort = port;
}
