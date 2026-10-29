using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 同步玩家地图画线。
// Synchronize participant map drawings.

public partial class SpectatorCombat
{
    private ulong _inventorySentAt;
    private long _inventorySerial;
    private string _lastInk = "";
    private double _inkTick;
    private ulong _inkViewer;

    // 玩家画线转换为地图坐标后发送；观众的线仍通过原有通道提交。
    // Relay participant ink in map coordinates; spectator ink keeps its existing transport.
    private void HostInk()
    {
        if (_inkViewer != _session.ViewerId) { _inkViewer = _session.ViewerId; _lastInk = ""; }
        // 背包不依赖地图是否打开，每秒同步一次。
        // Inventory updates once per second even while the host map is closed.
        if (Time.GetTicksMsec() - _inventorySentAt >= 1000)
        {
            _inventorySentAt = Time.GetTicksMsec();
            var run = RunManager.Instance.DebugOnlyGetState();
            if (run != null)
            {
                var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter { WarnOnGrow = false };
                writer.WriteInt(run.Players.Count);
                foreach (var player in run.Players) writer.Write(player.ToSerializable());
                _session.SendFragments("inventory", _session.CurrentMapKey, ++_inventorySerial, writer.Buffer.AsSpan(0, writer.BytePosition).ToArray());
            }
        }
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
                    coords[i * 2] = position.X; coords[i * 2 + 1] = position.Y;
                }
                if (coords.Length >= 4) lines.Add(new PlayerInk(coords, model.Character.MapDrawingColor.ToHtml(), player.playerId, line.isEraser));
            }
        }
        string json = JsonSerializer.Serialize(lines);
        if (json == _lastInk) return;
        _lastInk = json;
        // 地图画线也可在非战斗阶段同步，使用地图编号隔离。
        // Participant ink may sync outside combat, isolated by the current map key.
        _session.SendFragments("ink", _session.CurrentMapKey, 0, System.Text.Encoding.UTF8.GetBytes(json));
    }

    private void ReceiveInk(byte[] bytes)
    {
        var lines = JsonSerializer.Deserialize<PlayerInk[]>(bytes);
        if (lines is not { Length: <= 512 } || lines.Any(line => line.Points is not { Length: >= 4 and <= 512 }
            || line.Points.Length % 2 != 0 || line.Points.Any(value => !float.IsFinite(value) || Math.Abs(value) > 10000)
            || line.Color is not { Length: <= 9 } || !Color.HtmlIsValid(line.Color))) return;
        if (lines.Select(line => line.PlayerId).Distinct().Count() > 4) return;
        _map.SetPlayerInk(lines);
    }

    internal sealed record PlayerInk(float[] Points, string Color, ulong PlayerId, bool Eraser = false);
}
