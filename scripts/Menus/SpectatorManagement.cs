using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using Steamworks;
using MegaCrit.Sts2.Core.Platform.Steam;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 管理观众并显示观战头像。
// Manage viewers and display spectator portraits.

public partial class SpectatorPanel
{
    private string _managementState = "";
    private Control? _badge;
    private MegaLabel? _badgeName;

    // 管理页面复用原生好友列表、返回按钮和蓝色操作按钮。
    // Reuse the native friends panel, back button, and blue action button for management.
    public void OpenManagement(NPauseMenu menu)
    {
        if (!_session.IsHost) return;
        _managementScreen = CreateScreen("SpectatorManagement");
        Push(menu, _managementScreen);
    }

    private void RefreshManagement()
    {
        if (_managementScreen == null) return;
        var toggle = _managementScreen.GetNode<Control>("%RefreshButton");
        toggle.OffsetLeft = -250;
        toggle.OffsetRight = 250;
        toggle.GetNode<MegaLabel>("Label").SetTextAutoSize(_session.AllowSpectators ? L.T("management.enabled") : L.T("management.disabled"));
        var container = ClearList(_managementScreen, L.T("management.empty")
            + (_session.IsLocalHost ? "\n" + _session.LocalHostStatus : ""), !_session.HasViewer && _session.Requests.Count == 0);
        foreach (var viewer in _session.Roster.Values)
            AddFriend(container, viewer.Id, L.T("management.kick"), () => _session.KickViewer(viewer.Id), viewer.Name);
        foreach (var request in _session.Requests.ToArray())
            AddFriend(container, request.Key, L.T("management.approve"), () => _session.ApproveViewer(request.Key), request.Value.Name);
        ActiveScreenContext.Instance.Update();
    }

    // 只复制原生头像和名字控件，作为列表旁的独立展示，不伪造玩家。
    // Copy native portrait and name controls as a separate display without faking a player.
    private readonly Dictionary<ulong, (Control Root, MegaLabel Name)> _badges = new();
    private void UpdateBadge()
    {
        var players = NRun.Instance?.GlobalUi?.MultiplayerPlayerContainer;
        var mapPlayers = _session.Watching && _map.Visible ? _map.GetNodeOrNull<VBoxContainer>("SpectatorPlayers") : null;
        Node? parent = mapPlayers ?? players?.GetParent();
        var viewers = _session.Roster.Values.ToArray();
        foreach (var id in _badges.Keys.ToArray())
            if (!GodotObject.IsInstanceValid(_badges[id].Root) || _badges[id].Root.GetParent() != parent || !viewers.Any(viewer => viewer.Id == id))
            {
                if (GodotObject.IsInstanceValid(_badges[id].Root)) _badges[id].Root.QueueFree();
                _badges.Remove(id);
            }
        if (parent == null) return;
        float bottom = players?.GetChildren().OfType<NMultiplayerPlayerState>()
            .Select(player => player.Position.Y + Math.Max(player.Size.Y, 64)).DefaultIfEmpty(0).Max() ?? 0;
        for (int index = 0; index < viewers.Length; index++)
        {
            var viewer = viewers[index];
            if (!_badges.TryGetValue(viewer.Id, out var badge))
            {
                var visuals = RosterVisuals.Create(ResourceLoader.Load<Texture2D>("res://images/ui/top_panel/character_icon_defect.png"));
                visuals.Root.Name = "SpectatorBadge" + viewer.Id;
                var caption = (MegaLabel)visuals.Name.Duplicate();
                caption.Position = new Vector2(67, 34); caption.Text = L.T("common.watching");
                visuals.Root.AddChild(caption); parent.AddChild(visuals.Root);
                badge = (visuals.Root, visuals.Name); _badges.Add(viewer.Id, badge);
            }
            badge.Name.SetTextAutoSize(viewer.Name);
            if (mapPlayers == null && players != null)
            {
                badge.Root.Position = players.Position + new Vector2(0, bottom + 8 + index * 64);
                badge.Root.Modulate = players.Modulate;
            }
            badge.Root.Visible = mapPlayers != null || players?.IsVisibleInTree() == true
                && (NMapScreen.Instance?.IsOpen == true || NRun.Instance?.CombatRoom != null);
            if (index == 0) { _badge = badge.Root; _badgeName = badge.Name; }
        }
    }
}
