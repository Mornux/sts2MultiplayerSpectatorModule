using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

internal sealed record PlayerInk(float[] Points, string Color, ulong PlayerId, bool Eraser = false);

// 地图画线与背包独立采集，不依赖是否正在播放战斗。
// Capture map ink and inventories independently of combat playback.
internal sealed class MapStateSync(SpectatorSession session, SpectatorMap map)
{
    private ulong _inventorySentAt, _inkViewer;
    private long _inventorySerial;
    private double _tick;
    private string _lastInk = "", _mapKey = "";
    private string? _inventoryError, _inkError;

    internal void Tick(double delta)
    {
        if (!session.IsHost || !session.HasViewer)
        {
            _lastInk = "";
            _inkViewer = 0;
            return;
        }
        if ((_tick += delta) < 0.3) return;
        _tick = 0;
        if (_inkViewer != session.ViewerRevision || _mapKey != session.CurrentMapKey)
        {
            _inkViewer = session.ViewerRevision;
            _mapKey = session.CurrentMapKey;
            _lastInk = "";
        }
        ulong now = Time.GetTicksMsec();
        if (now - _inventorySentAt >= 1000)
        {
            _inventorySentAt = now;
            try { CaptureInventory(); _inventoryError = null; }
            catch (Exception error) { WarnOnce(ref _inventoryError, "Inventory capture", error); }
        }
        // 单项失败不阻止另一项同步，也不影响正式玩家的帧循环。
        // One capture failure cannot suppress the other stream or interrupt native play.
        try { CaptureInk(); _inkError = null; }
        catch (Exception error) { WarnOnce(ref _inkError, "Player ink capture", error); }
    }

    private void CaptureInventory()
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (run == null) return;
        var writer = new PacketWriter { WarnOnGrow = false };
        writer.WriteInt(run.Players.Count);
        foreach (var player in run.Players) writer.Write(player.ToSerializable());
        session.SendFragments("inventory", session.CurrentMapKey, ++_inventorySerial,
            writer.Buffer.AsSpan(0, writer.BytePosition).ToArray());
    }

    private void CaptureInk()
    {
        var screen = NMapScreen.Instance;
        if (screen?.Drawings == null) return;
        var points = screen.GetNode<Control>("TheMap/Points");
        var drawings = screen.Drawings;
        var state = RunManager.Instance.DebugOnlyGetState();
        if (state == null) return;
        var lines = new List<PlayerInk>();
        var toPoints = points.GetGlobalTransform().AffineInverse() * drawings.GetGlobalTransform();
        var scale = new Vector2(960, drawings.Size.Y);
        var center = new Vector2(drawings.Size.X / 2, 0);
        foreach (var player in drawings.GetSerializableMapDrawings().drawings)
        {
            var model = state.Players.FirstOrDefault(item => item.NetId == player.playerId);
            if (model == null) continue;
            foreach (var line in player.lines.Take(128))
            {
                var coords = new float[Math.Min(256, line.mapPoints.Count) * 2];
                for (int i = 0; i < coords.Length / 2; i++)
                {
                    var position = toPoints * ((line.mapPoints[i] * scale + center) * 2) - screen.Size / 2;
                    coords[i * 2] = position.X;
                    coords[i * 2 + 1] = position.Y;
                }
                if (coords.Length >= 4)
                    lines.Add(new PlayerInk(coords, model.Character.MapDrawingColor.ToHtml(), player.playerId, line.isEraser));
            }
        }
        string json = JsonSerializer.Serialize(lines);
        if (json == _lastInk) return;
        session.SendFragments("ink", session.CurrentMapKey, 0, Encoding.UTF8.GetBytes(json));
        _lastInk = json;
    }

    internal void ReceiveInk(byte[] bytes)
    {
        if (!session.Watching) return;
        try
        {
            var lines = JsonSerializer.Deserialize<PlayerInk[]>(bytes);
            if (lines is not { Length: <= 512 } || lines.Any(line => line == null
                || line.Points is not { Length: >= 4 and <= 512 } || line.Points.Length % 2 != 0
                || line.Points.Any(value => !float.IsFinite(value) || Math.Abs(value) > 10000)
                || line.Color is not { Length: <= 9 } || !Color.HtmlIsValid(line.Color))) return;
            if (lines.Select(line => line.PlayerId).Distinct().Count() > 4) return;
            map.SetPlayerInk(lines);
        }
        catch (Exception error) { WarnOnce(ref _inkError, "Player ink receive", error); }
    }

    private static void WarnOnce(ref string? previous, string operation, Exception error)
    {
        string detail = $"{operation}: {error.GetType().Name}: {error.Message}";
        if (previous == detail) return;
        previous = detail;
        GD.PushWarning("[Spectator] " + detail);
    }
}
