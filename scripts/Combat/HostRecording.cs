using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 记录并发送房主战斗。
// Record and send host combat.

public partial class SpectatorCombat
{
    private string _hostCombat = "", _combat = "";
    private long _hostSequence, _nextSequence = 1, _endSequence = -1, _remoteTick;
    private CombatReplay? _hostReplay;
    private int _hostEventsSent;

    // 房主在原版记录初始状态后发送起点，后续事件按原版队列顺序增量发送。
    // Send the host's native initial state, then stream events in native queue order.
    internal void HostCapture(CombatReplayWriter writer)
    {
        if (!_session.IsHost || !_session.HasViewer || !writer.IsEnabled) return;
        var replay = (CombatReplay?)AccessTools.Field(typeof(CombatReplayWriter), "_replay").GetValue(writer);
        if (replay == null) return;
        _hostCombat = "";
        _hostReplay = replay;
        _hostEventsSent = 0;
    }

    internal void HostBegin()
    {
        if (!_session.IsHost || !_session.HasViewer || _hostReplay == null) return;
        // 原版会为所有房间创建回放起点，只有真正开战才发送。
        // Native snapshots exist for every room; start streaming only when combat is set up.
        if (RunManager.Instance.DebugOnlyGetState() is not { CurrentRoomCount: 1, CurrentRoom: CombatRoom }) return;
        var replay = _hostReplay;
        _hostCombat = Guid.NewGuid().ToString("N");
        _hostSequence = replay.events.Count;
        _hostEventsSent = replay.events.Count;
        Send("begin", 0, replay);
    }

    internal void HostEvent(CombatReplayWriter writer)
    {
        if (!_session.IsHost || !_session.HasViewer || _hostCombat.Length == 0) return;
        var replay = (CombatReplay?)AccessTools.Field(typeof(CombatReplayWriter), "_replay").GetValue(writer);
        if (replay == null || !ReferenceEquals(replay, _hostReplay)) return;
        while (_hostEventsSent < replay.events.Count) Send("event", ++_hostSequence, replay.events[_hostEventsSent++]);
    }

    private void OnCombatEnded(CombatRoom room)
    {
        if (!_session.IsHost || _hostCombat.Length == 0) return;
        SendBytes("end", _hostSequence, Array.Empty<byte>());
        _hostCombat = "";
    }

    private void Send<T>(string kind, long sequence, T value) where T : IPacketSerializable
    {
        var writer = new PacketWriter { WarnOnGrow = false };
        writer.Write(value);
        SendBytes(kind, sequence, writer.Buffer.AsSpan(0, writer.BytePosition).ToArray());
    }

    private void SendBytes(string kind, long sequence, byte[] bytes) =>
        _session.SendFragments(kind, _hostCombat, sequence, bytes);
}
