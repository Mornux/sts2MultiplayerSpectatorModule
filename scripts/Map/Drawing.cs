using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Saves.MapDrawing;
using MegaCrit.Sts2.Core.Unlocks;
using static sts2MultiplayerSpectatorModule.Scripts.SpectatorSession;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 处理观战地图绘画。
// Handle spectator map drawing.

public partial class SpectatorMap
{
    private readonly List<Vector2> _stroke = new();
    private Sketch[] _shownInk = Array.Empty<Sketch>();
    private DrawingMode _strokeMode;
    private string _strokeId = "";
    private long _strokeRevision;
    private ulong _lastStrokeSend;
    private ulong _lastSound;
    private SpectatorCombat.PlayerInk[] _playerInk = Array.Empty<SpectatorCombat.PlayerInk>();
    private int _playerInkVersion, _shownPlayerInkVersion;
    private string? _playerInkKey;
    internal const ulong DisplayPlayerId = ulong.MaxValue;
    private string _shownColor = "";

    // 玩家原版绘画按作者独立渲染，橡皮仍使用原版遮罩，不擦除其他作者。
    // Render native participant ink per author, preserving the native eraser mask.
    internal void SetPlayerInk(SpectatorCombat.PlayerInk[] ink)
    {
        _playerInk = ink;
        _playerInkKey = _session.Map?.Key;
        _playerInkVersion++;
        Refresh();
    }

    internal Color? InkColor(ulong playerId) => playerId == DisplayPlayerId ? new Color(_session.DrawingColor)
        : Snapshot?.Players?.FirstOrDefault(player => player.Id == playerId) is { } player ? new Color(player.Color)
        : _playerInk.FirstOrDefault(line => line.PlayerId == playerId) is { } ink ? new Color(ink.Color) : null;

    // 保留原版绘画输入、光标和渲染，只将画线消息转入独立观战通道。
    // Keep native drawing input, cursors, and rendering, routing ink through the spectator channel.
    private Vector2 ToMap(Vector2 position) => _native!.GetNode<Control>("TheMap/Points").GetGlobalTransform().AffineInverse()
        * (_native.Drawings.GetGlobalTransform() * position) - _native.Size / 2;

    internal void BeginStroke(Vector2 position, DrawingMode? mode)
    {
        _stroke.Clear();
        _strokeId = Guid.NewGuid().ToString("N"); _strokeRevision = 0;
        _lastStrokeSend = 0;
        _strokeMode = mode ?? _native!.Drawings.GetLocalDrawingMode();
        ExtendStroke(position);
    }

    internal void ExtendStroke(Vector2 position)
    {
        var point = ToMap(position);
        if (_stroke.Count == 0 || _stroke[^1].DistanceTo(point) >= 6)
        {
            _stroke.Add(point);
            if (_stroke.Count >= 96)
            { SubmitStroke(); _strokeId = Guid.NewGuid().ToString("N"); _strokeRevision = 0; _stroke.Add(point); }
            else if (_stroke.Count >= 2 && Time.GetTicksMsec() - _lastStrokeSend >= 50)
            { _lastStrokeSend = Time.GetTicksMsec(); SubmitStroke(true); }
        }
    }

    private void SubmitStroke(bool live = false)
    {
        var points = new float[_stroke.Count * 2];
        for (int i = 0; i < _stroke.Count; i++) { points[i * 2] = _stroke[i].X; points[i * 2 + 1] = _stroke[i].Y; }
        if (points.Length == 2 && _strokeMode == DrawingMode.Erasing) points = [points[0], points[1], points[0], points[1]];
        if (!live) _stroke.Clear();
        if (points.Length >= 4 && _key == _session.Map?.Key) _session.SubmitSketch(points, _strokeId, ++_strokeRevision, live, _strokeMode == DrawingMode.Erasing);
    }

    internal void EndStroke()
    {
        SubmitStroke();
        RefreshInk(true);
    }

    internal void ClearInk() { _session.ClearSketches(); RefreshInk(true); }

    private void RefreshInk(bool force = false)
    {
        if (_native == null || _native.Drawings.IsLocalDrawing()) return;
        if (!force && _shownInk.SequenceEqual(_session.Ink) && _shownPlayerInkVersion == _playerInkVersion
            && _shownColor == _session.DrawingColor) return;
        _shownInk = _session.Ink.ToArray();
        _shownColor = _session.DrawingColor;
        _shownPlayerInkVersion = _playerInkVersion;
        var drawings = _native.Drawings;
        drawings.ClearAllLines();
        var player = new SerializablePlayerMapDrawings { playerId = _displayRun!.Players[0].NetId };
        var toDrawings = drawings.GetGlobalTransform().AffineInverse() * _native.GetNode<Control>("TheMap/Points").GetGlobalTransform();
        SerializableMapDrawingLine ConvertLine(float[] coords, bool eraser = false)
        {
            var line = new SerializableMapDrawingLine { isEraser = eraser };
            for (int i = 0; i < coords.Length; i += 2)
            {
                var mapPoint = new Vector2(coords[i], coords[i + 1]) + _native.Size / 2;
                var position = (toDrawings * mapPoint) * 0.5f;
                line.mapPoints.Add(new Vector2((position.X - drawings.Size.X / 2) / 960, position.Y / drawings.Size.Y));
            }
            return line;
        }
        player.lines.AddRange(_shownInk.Select(sketch => ConvertLine(sketch.Points, sketch.Eraser)));
        var data = new SerializableMapDrawings { drawings = new() { player } };
        if (_playerInkKey == _session.Map?.Key)
            foreach (var author in _playerInk.GroupBy(line => line.PlayerId))
            {
                if (author.Key == player.playerId) continue;
                if (!_displayRun.Players.Any(model => model.NetId == author.Key))
                {
                    var model = MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun<Defect>(UnlockState.all, author.Key);
                    Set(model, "RunState", _displayRun);
                    ((List<MegaCrit.Sts2.Core.Entities.Players.Player>)_displayRun.Players).Add(model);
                }
                data.drawings.Add(new SerializablePlayerMapDrawings
                {
                    playerId = author.Key, lines = author.Select(line => ConvertLine(line.Points, line.Eraser)).ToList()
                });
            }
        drawings.LoadDrawings(data);
    }

    private void StopDrawing()
    {
        if (_native == null) return;
        var input = (NMapDrawingInput?)AccessTools.Field(typeof(NMapScreen), "_drawingInput").GetValue(_native);
        if (input != null && GodotObject.IsInstanceValid(input) && !input.IsQueuedForDeletion()) input.StopDrawing();
        Set(_native, "_isDragging", false);
    }

    internal void ClickNode()
    {
        if (_pause?.Visible == true || _native!.Drawings.GetLocalDrawingMode() != DrawingMode.None
            || Time.GetTicksMsec() - _lastSound < 150) return;
        _lastSound = Time.GetTicksMsec();
        SfxCmd.Play("event:/sfx/ui/map/map_select");
    }
}
