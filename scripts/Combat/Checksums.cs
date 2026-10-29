using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 核对战斗同步校验值。
// Compare combat synchronization checksums.

public partial class SpectatorCombat
{
    private bool _checksumWarning;
    private readonly Dictionary<uint, uint> _hostChecksums = new(), _localChecksums = new();

    // 校验差异仅诊断，不中断只读观看；执行异常和超时仍由回放层处理。
    // Diagnose checksum differences without interrupting read-only playback; execution failures still terminate it.
    internal void Checksum(NetChecksumData checksum)
    {
        if (!CombatManager.Instance.IsInProgress) return;
        if (_session.IsHost && _hostCombat.Length != 0) Send("check", checksum.id, checksum);
        else if (_session.ReplayRunning && !_closing) CheckHash(checksum, false);
    }

    private void CheckHash(NetChecksumData checksum, bool fromHost)
    {
        var own = fromHost ? _hostChecksums : _localChecksums;
        var other = fromHost ? _localChecksums : _hostChecksums;
        if (other.Remove(checksum.id, out uint expected))
        {
            if (expected != checksum.checksum && !_checksumWarning)
            {
                _checksumWarning = true;
                GD.PushWarning($"[Spectator] Replay checksum differs at {checksum.id}: host={(fromHost ? checksum.checksum : expected):x8}, viewer={(fromHost ? expected : checksum.checksum):x8}. Continuing read-only playback.");
            }
            return;
        }
        own[checksum.id] = checksum.checksum;
        if (own.Count > 256) own.Remove(own.Keys.Min());
    }
}
