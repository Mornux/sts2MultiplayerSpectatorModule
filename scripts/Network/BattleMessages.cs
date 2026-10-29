
namespace sts2MultiplayerSpectatorModule.Scripts;

// 分片并组装大型战斗消息。
// Fragment and assemble large combat messages.

// 分片边界独立于 Godot，可离线验证乱序、重复和过量消息。
// Keep fragment validation independent of Godot for offline ordering and bounds checks.
internal sealed record BattleFrame(string Kind, string Combat, long Sequence, long Tick, int Part, int Parts, string Data)
{
    // 战斗、事件和地图画线共用分片规则，空消息也发送一个分片。
    // Share fragmentation across combat, events and ink, including one fragment for empty payloads.
    internal static IEnumerable<BattleFrame> Split(string kind, string key, long sequence, long tick, byte[] bytes)
    {
        if (bytes.Length > BattleAssembler.MaxBytes) yield break;
        int parts = Math.Max(1, (bytes.Length + BattleAssembler.PartBytes - 1) / BattleAssembler.PartBytes);
        for (int part = 0; part < parts; part++)
        {
            int offset = part * BattleAssembler.PartBytes;
            yield return new(kind, key, sequence, tick, part, parts,
                Convert.ToBase64String(bytes.AsSpan(offset, Math.Min(BattleAssembler.PartBytes, bytes.Length - offset))));
        }
    }
}

internal sealed class BattleAssembler
{
    internal const int PartBytes = 12000, MaxBytes = 2 * 1024 * 1024;
    private sealed class Assembly(int parts, ulong tick)
    {
        internal readonly byte[]?[] Parts = new byte[parts][];
        internal readonly ulong Started = tick;
        internal int Bytes, Count;
    }
    private readonly Dictionary<(string, string, long), Assembly> _pending = new();
    internal byte[]? Accept(BattleFrame frame, ulong tick)
    {
        if (frame.Kind is not ("begin" or "event" or "end" or "ink" or "check" or "event-view" or "inventory") || frame.Combat is not { Length: 32 }
            || frame.Sequence is < 0 or > 100000 || frame.Tick < 0 || frame.Parts is < 1 or > 175
            || frame.Part < 0 || frame.Part >= frame.Parts || frame.Data is not { Length: <= 16000 }) return null;
        byte[] bytes = Convert.FromBase64String(frame.Data);
        if (bytes.Length > PartBytes) return null;
        var key = (frame.Kind, frame.Combat, frame.Sequence);
        if (!_pending.TryGetValue(key, out var pending))
        {
            if (_pending.Count >= 32) return null;
            _pending[key] = pending = new Assembly(frame.Parts, tick);
        }
        if (pending.Parts.Length != frame.Parts) return null;
        if (pending.Parts[frame.Part] == null)
        {
            if (pending.Bytes + bytes.Length > MaxBytes || _pending.Values.Sum(item => item.Bytes) + bytes.Length > MaxBytes * 2)
            { _pending.Remove(key); return null; }
            pending.Parts[frame.Part] = bytes;
            pending.Bytes += bytes.Length;
            pending.Count++;
        }
        if (pending.Count != frame.Parts) return null;
        _pending.Remove(key);
        return pending.Parts.SelectMany(part => part!).ToArray();
    }
    internal void Expire(ulong tick)
    {
        foreach (var key in _pending.Where(pair => tick >= pair.Value.Started && tick - pair.Value.Started > 15000).Select(pair => pair.Key).ToArray()) _pending.Remove(key);
    }
    internal void Clear() => _pending.Clear();
}
