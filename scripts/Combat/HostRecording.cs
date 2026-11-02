using System.Reflection;
using Godot;
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
    private string? _hostCaptureError;
    private static readonly Lazy<FieldInfo> ReplayField = new(() =>
        AccessTools.Field(typeof(CombatReplayWriter), "_replay")
        ?? throw new MissingFieldException(typeof(CombatReplayWriter).FullName, "_replay"));

    private static CombatReplay? RecordedReplay(CombatReplayWriter writer)
    {
        var field = ReplayField.Value;
        if (!typeof(CombatReplay).IsAssignableFrom(field.FieldType))
            throw new InvalidOperationException($"CombatReplayWriter._replay has unsupported type '{field.FieldType.FullName}'.");
        return (CombatReplay?)field.GetValue(writer);
    }

    // 房主在原版记录初始状态后发送起点，后续事件按原版队列顺序增量发送。
    // Send the host's native initial state, then stream events in native queue order.
    internal void HostCapture(CombatReplayWriter writer)
    {
        try
        {
            if (!_session.IsHost || !_session.HasViewer || !writer.IsEnabled) return;
            var replay = RecordedReplay(writer);
            if (replay == null) return;
            _hostCombat = "";
            _hostReplay = replay;
            _hostEventsSent = 0;
            _hostCaptureError = null;
        }
        catch (Exception error) { StopHostCapture("initial state", error); }
    }

    internal void HostBegin()
    {
        try
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
        catch (Exception error) { StopHostCapture("battle start", error); }
    }

    internal void HostEvent(CombatReplayWriter writer)
    {
        try
        {
            if (!_session.IsHost || !_session.HasViewer || _hostCombat.Length == 0) return;
            var replay = RecordedReplay(writer);
            if (replay == null || !ReferenceEquals(replay, _hostReplay)) return;
            while (_hostEventsSent < replay.events.Count)
            {
                Send("event", _hostSequence + 1, replay.events[_hostEventsSent]);
                _hostSequence++;
                _hostEventsSent++;
            }
        }
        catch (Exception error) { StopHostCapture("battle event", error); }
    }

    private void OnCombatEnded(CombatRoom room)
    {
        try
        {
            if (!_session.IsHost || _hostCombat.Length == 0) return;
            SendBytes("end", _hostSequence, Array.Empty<byte>());
            _hostCombat = "";
            _hostReplay = null;
        }
        catch (Exception error) { StopHostCapture("battle end", error); }
    }

    // 采集失败只能停止观战流，不能把异常传回原版战斗回调。
    // A capture failure stops only the spectator stream, never the native battle callback.
    private void StopHostCapture(string operation, Exception error)
    {
        string combat = _hostCombat;
        _hostCombat = "";
        _hostReplay = null;
        _hostEventsSent = 0;
        string detail = $"{operation}: {error.GetType().Name}: {error.Message}";
        if (_hostCaptureError != detail)
        {
            _hostCaptureError = detail;
            GD.PushWarning("[Spectator] Combat capture stopped during " + detail);
        }
        // 已发出的事件仍可完成；尽力通知观众结束，不重试失败事件。
        // Let already sent events finish; best-effort close without retrying the failed event.
        if (combat.Length == 0) return;
        try { _session.SendFragments("end", combat, _hostSequence, Array.Empty<byte>()); }
        catch (Exception sendError) { GD.PushWarning("[Spectator] Combat capture close: " + sendError.Message); }
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
