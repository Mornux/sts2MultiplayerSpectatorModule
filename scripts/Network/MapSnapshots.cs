using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 采集并验证只读地图快照。
// Capture and validate read-only map snapshots.

public partial class SpectatorSession
{

    // 地图和画线快照不创建参战玩家；战斗另行传输只读回放。
    // Map and ink snapshots create no participant; combat uses a separate read-only replay stream.
    public sealed record MapNode(int Col, int Row, int Type, float X, float Y, int[] Children, bool Current, bool Visited);
    public sealed record MapSnapshot(string Key, int Act, int Floor, string Room, string Paper, MapNode[] Nodes, long RunTime = 0,
        string ActId = "", string BossId = "", string AncientId = "", string SecondBossId = "", string BaseRoom = "",
        MapPlayer[]? Players = null);
    public sealed record MapPlayer(ulong Id, string Name, string CharacterId, int Hp, int MaxHp, string Color);

    // 从原生地图读取实际位置和连线，观众只显示地图。
    // Read native node positions and edges; viewers render only the map.
    private string? Snapshot()
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (run?.Map == null) return null;
        // 原版首局可能没有先古，使用原版存档接口读取可空的房间模型。
        // Native first runs can omit the Ancient; use the save API's nullable room models.
        var rooms = run.Act.ToSave().SerializableRooms;
        if (rooms.BossId == null) return null;
        if (_mapAct != run.CurrentActIndex)
        {
            _mapAct = run.CurrentActIndex;
            _mapKey = Guid.NewGuid().ToString("N");
            ResetInk();
        }
        var points = run.Map.GetAllMapPoints().Append(run.Map.StartingMapPoint).Append(run.Map.BossMapPoint)
            .Concat(run.Map.SecondBossMapPoint is { } second ? new[] { second } : Array.Empty<MapPoint>())
            .Distinct().ToArray();
        var natives = NMapScreen.Instance?.GetNodeOrNull<Control>("TheMap/Points")?.GetChildren()
            .OfType<NMapPoint>().ToDictionary(node => node.Point.coord);
        var nodes = points.Select(point =>
        {
            var position = natives != null && natives.TryGetValue(point.coord, out var node)
                ? node.Position : new Vector2(point.coord.col * 150, -point.coord.row * 150);
            return new MapNode(point.coord.col, point.coord.row, (int)point.PointType, position.X, position.Y,
                point.Children.Select(child => Array.IndexOf(points, child)).Where(index => index >= 0).ToArray(),
                run.CurrentMapCoord == point.coord, run.VisitedMapCoords.Contains(point.coord));
        }).ToArray();
        return JsonSerializer.Serialize(new MapSnapshot(_mapKey, _mapAct, run.TotalFloor,
            run.CurrentRoom?.RoomType.ToString() ?? "Map", run.Act.MapMidBgPath, nodes, RunManager.Instance.RunTime,
            run.Act.Id.ToString(), rooms.BossId.ToString(), rooms.AncientId?.ToString() ?? "",
            rooms.SecondBossId?.ToString() ?? "", run.BaseRoom?.RoomType.ToString() ?? "",
            CapturePlayers(run, RunManager.Instance.NetService.Platform)));
    }

    // 名单只传输展示数据，不给观众增加参战玩家身份。
    // Send display-only roster data without creating a participant for the viewer.
    public static MapPlayer[] CapturePlayers(RunState run, PlatformType platform) => run.Players.Take(4).Select(player =>
        new MapPlayer(player.NetId, CleanName(PlatformUtil.GetPlayerNameRaw(platform, player.NetId)),
            player.Character.Id.ToString(), player.Creature.CurrentHp, player.Creature.MaxHp,
            player.Character.MapDrawingColor.ToHtml(false))).ToArray();

    // 校验地图和画线边界，拒绝超量、过期及非有限坐标。
    // Reject oversized, stale, or non-finite map and drawing data.
    public static bool ValidMap(MapSnapshot? map) => map is { Key.Length: 32, Paper.Length: <= 256, Room.Length: <= 128,
        Act: >= 0 and <= 100, Floor: >= 0, RunTime: >= 0 and <= 315360000, Nodes.Length: > 0 and <= 512 }
        && map.ActId is { Length: <= 128 } && map.BossId is { Length: <= 128 }
        && map.AncientId is { Length: <= 128 } && map.SecondBossId is { Length: <= 128 }
        && map.BaseRoom is { Length: <= 128 }
        && (map.Players == null || map.Players is { Length: <= 4 }
            && map.Players.All(player => player != null && player.Id != ulong.MaxValue
                && player.Name is { Length: <= 40 } && player.Name.All(ch => !char.IsControl(ch))
                && player.CharacterId is { Length: > 0 and <= 128 } && ValidColor(player.Color)
                && player.Hp >= 0 && player.MaxHp is > 0 and <= 999999999 && player.Hp <= player.MaxHp)
            && map.Players.Select(player => player.Id).Distinct().Count() == map.Players.Length)
        && map.Nodes.All(node => node != null && float.IsFinite(node.X) && float.IsFinite(node.Y)
            && Math.Abs(node.X) < 10000 && Math.Abs(node.Y) < 10000 && node.Type is >= 0 and <= 8
            && node.Col is >= 0 and < 64 && node.Row is >= 0 and < 128
            && node.Children is { Length: <= 16 } && node.Children.All(index => index >= 0 && index < map.Nodes.Length));
}
