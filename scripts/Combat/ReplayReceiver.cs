using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 接收并组装战斗消息。
// Receive and assemble combat messages.

public partial class SpectatorCombat
{
    private readonly BattleAssembler _assembler = new();
    private readonly List<string> _nextBattlePackets = new();
    private string _waitingBattle = "";
    private int _nextBattleBytes;

    // 有界分片重组，战斗编号和事件序号防止旧包污染新战斗。
    // Bound fragment assembly and isolate combat IDs and event sequences.
    private void Receive(string json)
    {
        if (!_session.Watching) return;
        try
        {
            var frame = JsonSerializer.Deserialize<BattleFrame>(json);
            if (frame?.Kind is "event-view" or "inventory") return;
            if (frame == null || _finished.Contains(frame.Combat)) return;
            if (frame.Combat == _waitingBattle)
            {
                if ((_nextBattleBytes += System.Text.Encoding.UTF8.GetByteCount(json)) > BattleAssembler.MaxBytes * 2)
                    throw new InvalidOperationException(L.T("combat.next_overflow"));
                _nextBattlePackets.Add(json);
                return;
            }
            var bytes = _assembler.Accept(frame, Time.GetTicksMsec());
            if (bytes == null) return;
            if (frame.Kind == "ink")
            { if (frame.Combat == _session.Map?.Key) ReceiveInk(bytes); return; }
            if (frame.Kind == "begin")
            {
                if (_combat == frame.Combat) return;
                var reader = new PacketReader();
                reader.Reset(bytes);
                var replay = reader.Read<CombatReplay>();
                if (replay.modelIdHash != ModelIdSerializationCache.Hash)
                    throw new InvalidOperationException(L.T("combat.models_differ"));
                string? version = MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo?.Version;
                if (version != null && replay.version != version)
                    throw new InvalidOperationException(L.T("combat.version_differ"));
                if (_session.ReplayRunning)
                {
                    _waitingBattle = frame.Combat;
                    _nextBattlePackets.Clear();
                    _nextBattleBytes = 0;
                    int count = Math.Max(1, (bytes.Length + BattleAssembler.PartBytes - 1) / BattleAssembler.PartBytes);
                    for (int part = 0; part < count; part++)
                    {
                        int offset = part * BattleAssembler.PartBytes;
                        var packet = frame with { Part = part, Parts = count, Data = Convert.ToBase64String(
                            bytes.AsSpan(offset, Math.Min(BattleAssembler.PartBytes, bytes.Length - offset))) };
                        string staged = JsonSerializer.Serialize(packet, SpectatorSession.WireJson);
                        _nextBattlePackets.Add(staged);
                        _nextBattleBytes += staged.Length;
                    }
                    QueueClose();
                    return;
                }
                _combat = frame.Combat;
                _events.Clear();
                for (int i = 0; i < replay.events.Count; i++) _events[i + 1] = (replay.events[i], frame.Tick);
                _checksumWarning = false;
                _hostChecksums.Clear();
                _localChecksums.Clear();
                _nextSequence = 1;
                _endSequence = -1;
                _remoteTick = frame.Tick;
                _localTick = Time.GetTicksMsec();
                _waitTime = 0;
                _loading = true;
                _liveCombatEnded = false;
                TaskHelper.RunSafely(StartPlayback(replay, ++_generation));
            }
            else if (frame.Combat == _combat && frame.Kind == "check")
            {
                var reader = new PacketReader();
                reader.Reset(bytes);
                CheckHash(reader.Read<NetChecksumData>(), true);
            }
            else if (frame.Combat == _combat && frame.Kind == "event" && frame.Sequence >= _nextSequence)
            {
                if (_events.Count >= 4096) throw new InvalidOperationException(L.T("combat.event_overflow"));
                var reader = new PacketReader();
                reader.Reset(bytes);
                _events.TryAdd(frame.Sequence, (reader.Read<CombatReplayEvent>(), frame.Tick));
            }
            else if (frame.Combat == _combat && frame.Kind == "end") _endSequence = frame.Sequence;
        }
        catch (Exception error) { Fail(error.Message); }
    }
}
