using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 同步绘画、橡皮与自定义颜色。
// Synchronize drawings, erasing and custom colors.

public partial class SpectatorSession
{
    private readonly List<Sketch> _ink = new();
    public string DrawingColor { get; private set; } = "dca747";
    internal string AuthorColor(ulong id) => Watching && id == SelfViewerId ? DrawingColor : Roster.GetValueOrDefault(id)?.Color ?? DrawingColor;
    // 橡皮和画笔共享轨迹同步，由原版减法材质实现局部擦除。
    // Share stroke synchronization for pen and eraser; native subtractive material handles partial erasing.
    public sealed record Sketch(string Key, int Act, float[] Points, string Stroke, long Revision, bool Live, bool Eraser, ulong Author);

    // 颜色按 RGB 同步，改色也会更新已经画出的观众笔迹。
    // Synchronize RGB colors, including spectator strokes already on the map.
    public static bool ValidColor(string? color) => SpectatorWireProtocol.ValidColor(color);
    public void SetDrawingColor(string color)
    {
        if (!ValidColor(color) || DrawingColor == color.ToLowerInvariant()) return;
        DrawingColor = color.ToLowerInvariant();
        if (_host != 0) Send(_host, new(SpectatorWireProtocol.Version, "color", _token, Color: DrawingColor));
        Updated?.Invoke();
    }
    private void UpdateViewerColor(ulong id, string color)
    {
        if (!ValidColor(color) || !_viewers.TryGetValue(id, out var viewer) || viewer.Color == color.ToLowerInvariant()) return;
        viewer.Color = color.ToLowerInvariant();
        PublishRoster(); SyncNativeInk();
    }
    public static bool ValidSketch(Sketch? sketch, string key, int act) => sketch is { Points.Length: >= 4 and <= 192 }
        && sketch.Key == key && sketch.Act == act && sketch.Points.Length % 2 == 0
        && sketch.Points.All(value => float.IsFinite(value) && Math.Abs(value) < 10000)
        && sketch.Stroke is { Length: 32 } && sketch.Revision is >= 1 and <= 1000000 && sketch.Author != 0;

    public void SubmitSketch(float[] points, string stroke, long revision, bool live, bool eraser)
    {
        if (_host == 0 || Map == null) return;
        var sketch = new Sketch(Map.Key, Map.Act, points, stroke, revision, live, eraser, SelfViewerId);
        if (!ValidSketch(sketch, Map.Key, Map.Act)) return;
        if (!ApplySketch(sketch)) return;
        Send(_host, new(SpectatorWireProtocol.Version, "draw", _token, JsonSerializer.Serialize(sketch)));
        Updated?.Invoke();
    }

    public void ClearSketches()
    {
        ClearAuthor(SelfViewerId);
        if (_host != 0) Send(_host, new(SpectatorWireProtocol.Version, "clear", _token));
        Updated?.Invoke();
    }

    private void RelayInk(string kind, string text)
    {
        if (!IsHost) return;
        foreach (ulong peer in ((NetHostGameService)RunManager.Instance.NetService).ConnectedPeers
            .Where(peer => peer.readyForBroadcasting).Select(peer => peer.peerId))
            if (peer != RunManager.Instance.NetService.NetId)
            {
                var packet = new SpectatorPacket(SpectatorWireProtocol.Version, kind, _mapKey, text);
                RunManager.Instance.NetService.SendMessage(new SpectatorLocalRelay
                    { Payload = JsonSerializer.Serialize(packet, WireJson) }, peer);
                if (!IsLocalHost) Send(peer, packet);
            }
    }

    // 原版控件拥有自己的展示身份，不污染真实跑局的玩家列表。
    // Give the native drawing control a display identity without adding a run participant.
    private void SyncNativeInk()
    {
        var map = NMapScreen.Instance;
        if (map?.Drawings is not { } native) return;
        var groups = _ink.GroupBy(line => line.Author).ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var overlay in native.GetParent().GetChildren().OfType<SpectatorNativeInk>())
            if (!groups.ContainsKey(overlay.Author)) { overlay.GetParent().RemoveChild(overlay); overlay.QueueFree(); }
        foreach (var group in groups)
        {
            string name = "SpectatorInk" + group.Key;
            var overlay = native.GetParent().GetNodeOrNull<SpectatorNativeInk>(name);
            if (overlay == null)
            {
                overlay = new SpectatorNativeInk { Name = name, Author = group.Key, Size = native.Size };
                overlay.InitializeInk(); native.GetParent().AddChild(overlay);
            }
            overlay.Sync(map, group.Value, AuthorColor(group.Key), RunManager.Instance.DebugOnlyGetState()?.CurrentActIndex ?? -1);
        }
    }

    // 同一笔画只更新已有轨迹，序号拒绝重复、延迟到达的旧片段。
    // Update a stroke in place and reject duplicate or late revisions.
    private bool ApplySketch(Sketch sketch)
    {
        int index = _ink.FindIndex(line => line.Author == sketch.Author && line.Stroke == sketch.Stroke);
        if (index >= 0)
        {
            if (_ink[index].Eraser != sketch.Eraser || _ink[index].Revision >= sketch.Revision || !_ink[index].Live && sketch.Live) return false;
            _ink[index] = sketch;
        }
        else
        {
            if (_ink.Count(line => line.Author == sketch.Author) >= 64) return false;
            _ink.Add(sketch);
        }
        return true;
    }

    private void ResetInk() { _ink.Clear(); if (!ReplayRunning && RunManager.Instance.IsInProgress) SyncNativeInk(); }
}
