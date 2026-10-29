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
    private double _lastSketch;
    private string _viewerColor = "dca747", _remoteViewerColor = "dca747";
    public string DrawingColor { get; private set; } = "dca747";
    public string InkColor => Watching ? DrawingColor : IsHost ? _viewerColor : _remoteViewerColor;
    public sealed record Sketch(string Key, int Act, float[] Points);

    // 颜色按 RGB 同步，改色也会更新已经画出的观众笔迹。
    // Synchronize RGB colors, including spectator strokes already on the map.
    public static bool ValidColor(string? color) => SpectatorWireProtocol.ValidColor(color);
    public void SetDrawingColor(string color)
    {
        if (!ValidColor(color) || DrawingColor == color.ToLowerInvariant()) return;
        DrawingColor = color.ToLowerInvariant();
        if (_host != 0) Send(_host, new(2, "color", _token, Color: DrawingColor));
        Updated?.Invoke();
    }
    private void UpdateViewerColor(string color)
    {
        if (!ValidColor(color) || _viewerColor == color.ToLowerInvariant()) return;
        _viewerColor = color.ToLowerInvariant();
        SyncNativeInk();
        RelayInk("color", "");
    }
    public static bool ValidSketch(Sketch? sketch, string key, int act) => sketch is { Points.Length: >= 4 and <= 192 }
        && sketch.Key == key && sketch.Act == act && sketch.Points.Length % 2 == 0
        && sketch.Points.All(value => float.IsFinite(value) && Math.Abs(value) < 10000);

    public void SubmitSketch(float[] points)
    {
        if (_host == 0 || Map == null || _ink.Count >= 64) return;
        var sketch = new Sketch(Map.Key, Map.Act, points);
        if (!ValidSketch(sketch, Map.Key, Map.Act)) return;
        _ink.Add(sketch);
        Send(_host, new(2, "draw", _token, JsonSerializer.Serialize(sketch)));
        Updated?.Invoke();
    }

    public void ClearSketches()
    {
        ResetInk();
        if (_host != 0) Send(_host, new(2, "clear", _token));
        Updated?.Invoke();
    }

    // 橡皮移除命中的整条观战笔迹，不影响参战玩家的画线。
    // Erase touched spectator strokes without altering participating players' ink.
    public void EraseSketchesAt(float x, float y)
    {
        if (_host == 0 || Map == null || !float.IsFinite(x) || !float.IsFinite(y)) return;
        if (_ink.RemoveAll(line => HitsSketch(line, x, y)) == 0) return;
        var eraser = new Sketch(Map.Key, Map.Act, new[] { x, y, x, y });
        Send(_host, new(2, "erase", _token, JsonSerializer.Serialize(eraser)));
        Updated?.Invoke();
    }

    public static bool HitsSketch(Sketch sketch, float x, float y)
    {
        for (int i = 0; i + 3 < sketch.Points.Length; i += 2)
        {
            float ax = sketch.Points[i], ay = sketch.Points[i + 1];
            float dx = sketch.Points[i + 2] - ax, dy = sketch.Points[i + 3] - ay;
            float length = dx * dx + dy * dy;
            float t = length == 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / length, 0, 1);
            float distanceX = x - ax - t * dx, distanceY = y - ay - t * dy;
            if (distanceX * distanceX + distanceY * distanceY <= 30 * 30) return true;
        }
        return false;
    }

    private void RelayInk(string kind, string text)
    {
        if (!IsHost) return;
        if (kind == "clear-ink") text = RunManager.Instance.DebugOnlyGetState()!.CurrentActIndex.ToString();
        foreach (ulong peer in ((NetHostGameService)RunManager.Instance.NetService).ConnectedPeers
            .Where(peer => peer.readyForBroadcasting).Select(peer => peer.peerId))
            if (peer != RunManager.Instance.NetService.NetId)
            {
                var packet = new SpectatorPacket(2, kind, _mapKey, text, Name: _viewerName, Color: _viewerColor);
                RunManager.Instance.NetService.SendMessage(new SpectatorLocalRelay
                    { Payload = JsonSerializer.Serialize(packet, WireJson) }, peer);
                if (!IsLocalHost) Send(peer, packet);
            }
    }

    // 画线作为独立叠层显示，不调用原版玩家绘图或投票接口。
    // Show ink in a separate overlay without native player drawing or voting calls.
    private void SyncNativeInk()
    {
        var points = NMapScreen.Instance?.GetNodeOrNull<Control>("TheMap/Points");
        if (points == null) return;
        var overlay = points.GetNodeOrNull<Node2D>("SpectatorInk");
        if (overlay == null) { overlay = new Node2D { Name = "SpectatorInk" }; points.AddChild(overlay); }
        overlay.Position = points.Size / 2;
        int act = RunManager.Instance.DebugOnlyGetState()?.CurrentActIndex ?? -1;
        foreach (var line in overlay.GetChildren().OfType<Line2D>()) line.DefaultColor = new Color(InkColor);
        if (overlay.GetChildCount() == _ink.Count && _ink.All(line => line.Act == act)) return;
        foreach (var child in overlay.GetChildren()) { overlay.RemoveChild(child); child.QueueFree(); }
        foreach (var sketch in _ink.Where(line => line.Act == act))
        {
            var line = new Line2D { Width = 6, DefaultColor = new Color(InkColor), Antialiased = true };
            for (int i = 0; i < sketch.Points.Length; i += 2) line.AddPoint(new Vector2(sketch.Points[i], sketch.Points[i + 1]));
            overlay.AddChild(line);
        }
    }

    private void ResetInk() { _ink.Clear(); if (!ReplayRunning && RunManager.Instance.IsInProgress) SyncNativeInk(); }
}
