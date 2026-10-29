using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.sts2.Core.Nodes.TopBar;
using static sts2MultiplayerSpectatorModule.Scripts.SpectatorSession;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 显示楼层、节点与计时器。
// Display floor, room and timer information.

public partial class SpectatorMap
{
    private MegaLabel? _timerLabel;

    // 保留原版所在节点、楼层、Boss、计时器与齿轮及其悬浮说明。
    // Retain native room, floor, boss, timer, and gear controls with their hover tips.
    private void CreateTopBar(Player player)
    {
        if (_topBar != null) { MoveChild(_topBar, -1); return; }
        _topBar = ResourceLoader.Load<PackedScene>("res://scenes/ui/top_bar.tscn").Instantiate<NTopBar>();
        _topBar.SetMeta("spectator_view", true);
        _topBar.GetNode<NRunTimer>("RightAlignedStuff/TimerContainer").SetMeta("spectator_view", true);
        // 未初始化牌堆的按钮需要跳过原版释放回调。
        // Skip native deck cleanup for the discarded, uninitialized button.
        foreach (var deck in _topBar.FindChildren("*", owned: false).OfType<NTopBarDeckButton>())
            deck.SetMeta("spectator_view", true);
        foreach (string path in new[] { "LeftAlignedStuff/TopBarHp", "LeftAlignedStuff/TopBarGold",
            "LeftAlignedStuff/PotionMarginifier", "LeftAlignedStuff/Modifiers",
            "RightAlignedStuff/SaveIndicator", "RightAlignedStuff/Map", "RightAlignedStuff/DeckContainer",
            "LeftAlignedStuff/PortraitContainer/TopBarPortraitTip/AscensionIcon",
            "LeftAlignedStuff/PortraitContainer/TopBarPortraitTip/AchievementLock", "ActiveScreenProxy", "GoldPopup" })
        {
            var node = _topBar.GetNodeOrNull(path);
            if (node != null) { node.GetParent().RemoveChild(node); node.Free(); }
        }
        // 按保留项裁剪右栏，避免牌堆控件层级变化后残留默认数量。
        // Keep only timer and gear so deck layout changes cannot leave a default card count.
        foreach (var node in _topBar.GetNode<Control>("RightAlignedStuff").GetChildren())
            if (node.Name != "TimerContainer" && node.Name != "PauseButton")
            { node.GetParent().RemoveChild(node); node.Free(); }
        _topBar.MouseFilter = MouseFilterEnum.Ignore;
        _topBar.GetNode<Control>("LeftAlignedStuff").MouseFilter = MouseFilterEnum.Ignore;
        _topBar.GetNode<Control>("RightAlignedStuff").MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_topBar);
        _topBar.GetNode<NTopBarPortrait>("%TopBarPortrait").Initialize(player);
        _topBar.GetNode<NTopBarRoomIcon>("%RoomIcon").Initialize(_displayRun!);
        _topBar.GetNode<NTopBarFloorIcon>("%FloorIcon").Initialize(_displayRun!);
        _topBar.GetNode<NTopBarBossIcon>("%BossIcon").Initialize(_displayRun!);
        _timerLabel = _topBar.GetNode<MegaLabel>("RightAlignedStuff/TimerContainer/TimerLabel");
    }

    // 给原版信息控件提供只读房间状态，悬浮文字与图标继续使用原版逻辑。
    // Supply read-only room state so native controls retain their icon and hover-tip logic.
    private void UpdateRoomInfo(MapSnapshot snapshot)
    {
        var visited = (List<MapCoord>)_displayRun!.VisitedMapCoords;
        visited.Clear();
        if (snapshot.Nodes.FirstOrDefault(node => node.Current) is { } current)
            visited.Add(new MapCoord(current.Col, current.Row));
        while (_displayRun.CurrentRoomCount > 0) _displayRun.PopCurrentRoom();
        _displayRun.PushRoom(new DisplayRoom(RoomTypeFor(snapshot)));
        _topBar!.GetNode<NTopBarRoomIcon>("%RoomIcon").Initialize(_displayRun);
        _topBar.GetNode<NTopBarFloorIcon>("%FloorIcon").GetNode<MegaLabel>("FloorNumLabel").SetTextAutoSize(snapshot.Floor.ToString());
        var boss = _topBar.GetNode<NTopBarBossIcon>("%BossIcon");
        boss.Initialize(_displayRun);
        Call(boss, "OnRoomEntered");
    }

    // 未知节点采用原始房间类型；事件中的战斗不会覆盖事件图标和说明。
    // Resolve unknown nodes from the base room so embedded combat preserves the event description.
    internal static RoomType RoomTypeFor(MapSnapshot snapshot)
    {
        var room = Enum.TryParse<RoomType>(snapshot.BaseRoom.Length > 0 ? snapshot.BaseRoom : snapshot.Room, out var type)
            && Enum.IsDefined(type) && type != RoomType.Unassigned ? type : RoomType.Map;
        if (snapshot.Nodes.FirstOrDefault(node => node.Current)?.Type == (int)MapPointType.Unknown
            && room is not (RoomType.Monster or RoomType.Treasure or RoomType.Shop or RoomType.Event)) room = RoomType.Event;
        return room;
    }

    private sealed class DisplayRoom(RoomType type) : AbstractRoom
    {
        public override RoomType RoomType => type;
        public override ModelId? ModelId => null;
        public override Task EnterInternal(IRunState? run, bool restoring) => Task.CompletedTask;
        public override Task Exit(IRunState? run) => Task.CompletedTask;
        public override Task Resume(AbstractRoom room, IRunState? run) => Task.CompletedTask;
    }
}
