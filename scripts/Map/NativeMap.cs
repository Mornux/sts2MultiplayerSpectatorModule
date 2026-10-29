using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using static sts2MultiplayerSpectatorModule.Scripts.SpectatorSession;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 构建原版地图与路线显示。
// Build native map and route visuals.

public partial class SpectatorMap
{

    // 只建立供原版节点读取的模型，不向 RunManager 注册、不加入玩家名额。
    // Build models for native visuals without registering a run or taking a player slot.
    private void CreateMap(MapSnapshot snapshot)
    {
        DestroyMap();
        var act = ActModel.FromSave(new SerializableActModel
        {
            Id = ModelId.Deserialize(snapshot.ActId),
            SerializableRooms = new SerializableRoomSet
            {
                EventIds = new(), NormalEncounterIds = new(), EliteEncounterIds = new(),
                BossId = ModelId.Deserialize(snapshot.BossId),
                AncientId = snapshot.AncientId.Length == 0 ? null : ModelId.Deserialize(snapshot.AncientId),
                SecondBossId = snapshot.SecondBossId.Length == 0 ? null : ModelId.Deserialize(snapshot.SecondBossId)
            }
        });
        var player = Player.CreateForNewRun<Defect>(UnlockState.all, DisplayPlayerId);
        _displayRun = RunState.CreateForTest(new[] { player }, seed: "SPECTATOR");
        Set(_displayRun, "Acts", new[] { act });
        _displayRun.Map = new DisplayActMap(snapshot);
        var replay = new NetReplayGameService(player.NetId);
        _input = new PeerInputSynchronizer(replay);
        _native = ResourceLoader.Load<PackedScene>("res://scenes/screens/map/map_screen.tscn").Instantiate<NMapScreen>();
        _native.SetMeta("spectator_view", true);
        Set(_native, "_runState", _displayRun);
        _native.GetNode<NMapBg>("TheMap/MapBg").Initialize(_displayRun);
        _native.GetNode<NMapDrawings>("TheMap/Drawings").Initialize(replay, _displayRun, _input);
        AddChild(_native);
        _native.GetNode<NMapMarker>("TheMap/MapMarker").Initialize(player);
        _native.SetMap(_displayRun.Map, 0, true);
        // 关闭原版可选节点的循环脉动，保留悬停、点击和子节点动画。
        // Stop selectable-node idle pulsing while keeping hover, clicks, and child animations.
        foreach (var point in _native.GetNode<Control>("TheMap/Points").GetChildren().OfType<NMapPoint>())
            point.SetProcess(false);
        Set(_native, "IsOpen", true);
        _native.Show();
        Call(_native.GetNode<NMapBg>("TheMap/MapBg"), "OnVisibilityChanged");
        CreateTopBar(player);
        CreatePauseMenu();
        ClearPlayerList();
        MoveChild(_status, -1);
        MoveChild(_hoverTips, -1);
        _key = snapshot.Key;
        _current = null;
        _shownInk = Array.Empty<Sketch>();
        _shownPlayerInkVersion = -1;
    }

    // 原版 Ready 中仅替换对跑局、投票及参战菜单的绑定。
    // Replace only run, voting, and participant menu bindings in native Ready.
    internal void BindNativeMap(NMapScreen map)
    {
        foreach (var binding in new (string Field, string Path)[]
        {
            ("_mapContainer", "TheMap"), ("_mapBgContainer", "TheMap/MapBg"),
            ("_pathsContainer", "TheMap/Paths"), ("_points", "TheMap/Points"),
            ("_marker", "TheMap/MapMarker"), ("Drawings", "TheMap/Drawings"),
            ("_shareButton", "%ShareButton"),
            ("_backButton", "Back"), ("_mapLegend", "MapLegend"),
            ("_legendItems", "MapLegend/LegendItems"), ("_legendHotkeyIcon", "%LegendHotkeyIcon"),
            ("_drawingToolsHotkeyIcon", "%DrawingToolsHotkey"), ("_backstop", "Backstop"),
            ("_drawingTools", "DrawingTools"), ("_mapDrawingButton", "%DrawButton"),
            ("_mapErasingButton", "%EraseButton"), ("_mapClearButton", "%ClearButton")
        }) Set(map, binding.Field, map.GetNode(binding.Path));
        map.GetNode<MegaLabel>("MapLegend/Header").SetTextAutoSize(new LocString("map", "LEGEND_HEADER").GetFormattedText());
        map.GetNode<NBackButton>("Back").Disable();
        map.GetNode<Control>("Back").Hide();
        map.GetNode<NBackButton>("Back").Connect(NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => ReturnToCombatRequested?.Invoke()));
        map.GetNode<Control>("%ShareButton").Hide();
        map.GetNode<Control>("%ShareToast").Hide();
        foreach (var binding in new[] { ("%DrawButton", "OnMapDrawingButtonPressed"),
            ("%EraseButton", "OnMapErasingButtonPressed"), ("%ClearButton", "OnClearMapDrawingButtonPressed") })
            map.GetNode<NButton>(binding.Item1).Connect(NClickableControl.SignalName.Released,
                Callable.From<NButton>(button => Call(map, binding.Item2, button)));
        map.GetNode<Control>("%LegendHotkeyIcon").Hide();
        map.GetNode<Control>("%DrawingToolsHotkey").Hide();
        Set(map, "_hasPlayedAnimation", true);
        SetCombatAvailable(_combatAvailable);
    }

    // 路径仍由原版生成；生成前恢复房主实际坐标，避免随机偏移不一致。
    // Keep native path generation and restore host coordinates before drawing paths.
    internal void RestorePositions()
    {
        var positions = _session.Map!.Nodes.ToDictionary(point => new MapCoord(point.Col, point.Row));
        foreach (var point in _native!.GetNode<Control>("TheMap/Points").GetChildren().OfType<NMapPoint>())
            if (positions.TryGetValue(point.Point.coord, out var source)) point.Position = new Vector2(source.X, source.Y);
    }

    // 同步已走路线与当前位置，不执行原版的可选路线和投票逻辑。
    // Update visited paths and current position without route selection or voting.
    private void UpdateProgress(MapSnapshot snapshot)
    {
        var sources = snapshot.Nodes.ToDictionary(point => new MapCoord(point.Col, point.Row));
        var nodes = _native!.GetNode<Control>("TheMap/Points").GetChildren().OfType<NMapPoint>().ToArray();
        foreach (var node in nodes)
        {
            node.State = sources[node.Point.coord].Visited ? MapPointState.Traveled : MapPointState.Untravelable;
            node.Enable();
            node.VoteContainer.Hide();
        }
        var paths = (Dictionary<(MapCoord, MapCoord), IReadOnlyList<TextureRect>>)AccessTools.Field(typeof(NMapScreen), "_paths").GetValue(_native)!;
        foreach (var (edge, ticks) in paths)
            foreach (var tick in ticks)
            {
                bool traveled = sources[edge.Item1].Visited && sources[edge.Item2].Visited;
                tick.Modulate = traveled ? _displayRun!.Act.MapTraveledColor : _displayRun!.Act.MapUntraveledColor;
                tick.Scale = traveled ? Vector2.One * 1.2f : Vector2.One;
            }
        var current = snapshot.Nodes.FirstOrDefault(node => node.Current);
        if (current == null) return;
        var coord = new MapCoord(current.Col, current.Row);
        if (_current == coord) return;
        bool first = _current == null;
        _current = coord;
        var marker = _native.GetNode<NMapMarker>("TheMap/MapMarker");
        marker.ResetMapPoint();
        marker.SetMapPoint(nodes.First(node => node.Point.coord == coord));
        if (!first) return;
        float scrollY = Mathf.Clamp(140 - current.Y, -600, 1800);
        Set(_native, "_targetDragPos", new Vector2(0, scrollY));
        _native.GetNode<Control>("TheMap").Position = new Vector2(0, scrollY);
    }

    // 适配房主拓扑，节点和连线仍交给原版 SetMap 创建。
    // Adapt the host topology and let native SetMap create nodes and paths.
    private sealed class DisplayActMap : ActMap
    {
        protected override MapPoint?[,] Grid { get; }
        public override MapPoint StartingMapPoint { get; }
        public override MapPoint BossMapPoint { get; }
        public override MapPoint? SecondBossMapPoint { get; }

        public DisplayActMap(MapSnapshot snapshot)
        {
            if (snapshot.Nodes.Select(point => (point.Col, point.Row)).Distinct().Count() != snapshot.Nodes.Length)
                throw new InvalidOperationException("Duplicate map coordinates.");
            var points = snapshot.Nodes.Select(node => new MapPoint(node.Col, node.Row) { PointType = (MapPointType)node.Type }).ToArray();
            StartingMapPoint = points.Where(point => point.coord.row == points.Min(item => item.coord.row)).Single();
            var bosses = points.Where(point => point.PointType == MapPointType.Boss).OrderBy(point => point.coord.row).ToArray();
            if (bosses.Length is < 1 or > 2) throw new InvalidOperationException("Missing native boss nodes.");
            BossMapPoint = bosses[0];
            SecondBossMapPoint = bosses.ElementAtOrDefault(1);
            Grid = new MapPoint[points.Max(point => point.coord.col) + 1, Math.Max(2, BossMapPoint.coord.row)];
            for (int i = 0; i < points.Length; i++)
            {
                var point = points[i];
                if (point != StartingMapPoint && !bosses.Contains(point)) Grid[point.coord.col, point.coord.row] = point;
                foreach (int child in snapshot.Nodes[i].Children) point.AddChildPoint(points[child]);
            }
        }
    }
}
