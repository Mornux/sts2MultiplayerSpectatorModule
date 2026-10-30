using System.Text.Json;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 每名观众独立鉴权、心跳和绘画限流；同一份游戏数据广播到至多四个连接。
// Authenticate, heartbeat and rate-limit each viewer independently; broadcast shared game data to at most four peers.
public partial class SpectatorSession
{
    internal const int MaxViewers = 4;
    private sealed class ViewerConnection(string token, string name, string color, double seen)
    {
        internal readonly string Token = token, Name = name;
        internal string Color = color;
        internal double Seen = seen, LastSketch = -1;
    }
    internal sealed record ViewerInfo(ulong Id, string Name, string Color);
    private sealed record ViewerRoster(ulong Self, ViewerInfo[] Viewers);
    private readonly Dictionary<ulong, ViewerConnection> _viewers = new();
    private readonly Dictionary<ulong, ViewerInfo> _remoteViewers = new();
    private ulong _selfViewerId;
    internal ulong ViewerRevision { get; private set; }
    internal IReadOnlyDictionary<ulong, ViewerInfo> Roster => IsHost
        ? _viewers.ToDictionary(pair => pair.Key, pair => new ViewerInfo(pair.Key, pair.Value.Name, pair.Value.Color)) : _remoteViewers;
    internal ulong SelfViewerId => _selfViewerId == 0 ? SpectatorMap.DisplayPlayerId : _selfViewerId;
    private bool AuthenticatedViewer(ulong id, string token) => IsHost && _viewers.TryGetValue(id, out var viewer) && viewer.Token == token;
    private void BroadcastViewers(SpectatorPacket packet)
    {
        foreach (var pair in _viewers.ToArray()) Send(pair.Key, packet with { Token = pair.Value.Token });
    }
    private void PublishRoster()
    {
        if (!IsHost) return;
        ViewerRevision++;
        var roster = Roster.Values.ToArray();
        RelayInk("viewer-roster", JsonSerializer.Serialize(new ViewerRoster(0, roster)));
        foreach (var pair in _viewers)
            Send(pair.Key, new(2, "viewer-roster", pair.Value.Token, JsonSerializer.Serialize(new ViewerRoster(pair.Key, roster))));
    }
    private void ClearAuthor(ulong author)
    {
        _ink.RemoveAll(line => line.Author == author);
        if (IsHost)
        {
            string text = author.ToString(System.Globalization.CultureInfo.InvariantCulture);
            RelayInk("viewer-clear", text); BroadcastViewers(new(2, "viewer-clear", "", text));
        }
        SyncNativeInk();
    }
    private bool HandleViewerData(ulong sender, SpectatorPacket packet)
    {
        if (AuthenticatedViewer(sender, packet.Token))
        {
            var viewer = _viewers[sender];
            if (packet.Kind == "color") { UpdateViewerColor(sender, packet.Color); return true; }
            if (packet.Kind == "leave") { RemoveViewer(sender, L.Wire("status.left")); return true; }
            if (packet.Kind == "clear") { ClearAuthor(sender); return true; }
            if (packet.Kind is "draw" or "erase")
            {
                var line = JsonSerializer.Deserialize<Sketch>(packet.Text);
                if (!ValidSketch(line, _mapKey, _mapAct) || line!.Live && _time - viewer.LastSketch < 0.035) return true;
                viewer.LastSketch = _time;
                line = line with { Author = sender, Eraser = line.Eraser || packet.Kind == "erase" };
                if (!ApplySketch(line)) return true;
                string json = JsonSerializer.Serialize(line);
                RelayInk("viewer-ink", json); BroadcastViewers(new(2, "viewer-ink", "", json)); SyncNativeInk();
                return true;
            }
        }
        if (!(Watching && sender == _host && packet.Token == _token || FromGameHost(sender))) return false;
        if (packet.Kind == "viewer-roster")
        {
            var roster = JsonSerializer.Deserialize<ViewerRoster>(packet.Text);
            if (roster?.Viewers is not { Length: <= MaxViewers } || roster.Viewers.Any(item => item.Id == 0 || item.Name.Length > 80 || !ValidColor(item.Color))
                || roster.Viewers.Select(item => item.Id).Distinct().Count() != roster.Viewers.Length) return true;
            _remoteViewers.Clear();
            foreach (var item in roster.Viewers) _remoteViewers.Add(item.Id, item);
            if (Watching) _selfViewerId = roster.Self;
            _remoteViewer = roster.Viewers.FirstOrDefault()?.Id ?? 0;
            _ink.RemoveAll(line => line.Author != 0 && !_remoteViewers.ContainsKey(line.Author));
            ViewerRevision++; SyncNativeInk(); Updated?.Invoke(); return true;
        }
        if (packet.Kind == "viewer-clear")
        {
            if (ulong.TryParse(packet.Text, out var author)) ClearAuthor(author);
            Updated?.Invoke(); return true;
        }
        if (packet.Kind == "viewer-ink")
        {
            var line = JsonSerializer.Deserialize<Sketch>(packet.Text);
            string key = Watching ? CurrentMapKey : packet.Token;
            int act = Watching ? Map?.Act ?? -1 : RunManager.Instance.DebugOnlyGetState()?.CurrentActIndex ?? -1;
            if (ValidSketch(line, key, act) && _remoteViewers.ContainsKey(line!.Author))
            {
                if (!Watching && _mapKey != key) { ResetInk(); _mapKey = key; }
                ApplySketch(line); SyncNativeInk(); Updated?.Invoke();
            }
            return true;
        }
        return false;
    }
}
