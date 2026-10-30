using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 复用原生菜单场景，观战地图和网络服务常驻。
// Reuse native menu scenes alongside the persistent spectator map and service.
public partial class SpectatorPanel : CanvasLayer
{
    public static SpectatorPanel? Instance { get; private set; }
    private readonly SpectatorSession _session = new();
    private readonly SpectatorMap _map = new();
    private SpectatorCombat _combat = null!;
    private NJoinFriendScreen? _friendsScreen, _managementScreen, _localScreen;
    private bool _refreshing;
    private int _refreshGeneration;
    public bool IsHost => _session.IsHost;

    public override void _Ready()
    {
        Instance = this;
        Layer = 100;
        ProcessMode = ProcessModeEnum.Always;
        AddChild(_session);
        AddChild(_map);
        _map.Initialize(_session);
        _map.Hide();
        _combat = new SpectatorCombat();
        AddChild(_combat);
        _combat.Initialize(_session, _map);
        var events = new Events.SpectatorEvents();
        AddChild(events);
        events.Initialize(_session, _map, _combat);
        var danmaku = new SpectatorDanmaku();
        AddChild(danmaku);
        danmaku.Initialize(_session);
        _map.ExitRequested += () => { _session.StopWatching(); _map.Hide(); };
        _session.FriendsChanged += RefreshList;
        _session.Updated += _map.Refresh;
    }

    public override void _Process(double delta)
    {
        UpdateBadge();
        if (!GodotObject.IsInstanceValid(_managementScreen) || !_managementScreen!.Visible) return;
        string state = $"{_session.IsHost}:{_session.AllowSpectators}:{_session.ViewerId}:{_session.LocalHostStatus}:{string.Join(",", _session.Requests.Select(pair => pair.Key + ":" + pair.Value.Approved))}";
        if (_managementState == state) return;
        _managementState = state;
        RefreshManagement();
    }

    public override void _ExitTree() => Instance = null;
}
