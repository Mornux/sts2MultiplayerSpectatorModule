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
    private ulong _badgeId;

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
        if (_session.HasViewer)
            AddFriend(container, _session.ViewerId, L.T("management.kick"), _session.KickViewer);
        foreach (var request in _session.Requests.ToArray())
            AddFriend(container, request.Key, L.T("management.approve"), () => _session.ApproveViewer(request.Key), request.Value.Name);
        ActiveScreenContext.Instance.Update();
    }

    // 只复制原生头像和名字控件，作为列表旁的独立展示，不伪造玩家。
    // Copy native portrait and name controls as a separate display without faking a player.
    private void UpdateBadge()
    {
        var players = NRun.Instance?.GlobalUi?.MultiplayerPlayerContainer;
        // 观战端显示自身，参战端显示房主同步的观众；地图和战斗共用同一头像。
        // Show self on the spectator client and the relayed viewer on players, in both map and combat.
        var mapPlayers = _session.Watching && _map.Visible ? _map.GetNodeOrNull<VBoxContainer>("SpectatorPlayers") : null;
        Node? badgeParent = mapPlayers ?? players?.GetParent();
        ulong id = _session.Watching ? SpectatorMap.DisplayPlayerId : _session.DisplayViewerId;
        if (badgeParent == null || id == 0)
        {
            if (GodotObject.IsInstanceValid(_badge)) _badge!.Hide();
            return;
        }
        if (!GodotObject.IsInstanceValid(_badge) || _badge!.GetParent() != badgeParent)
        {
            if (GodotObject.IsInstanceValid(_badge)) _badge!.QueueFree();
            var visuals = RosterVisuals.Create(ResourceLoader.Load<Texture2D>("res://images/ui/top_panel/character_icon_defect.png"));
            _badge = visuals.Root;
            _badge.Name = "SpectatorBadge";
            _badgeName = visuals.Name;
            var caption = (MegaLabel)_badgeName.Duplicate();
            caption.Position = new Vector2(67, 34);
            caption.Text = L.T("common.watching");
            _badge.AddChild(caption);
            badgeParent.AddChild(_badge);
        }
        if (_badgeId != id)
        {
            _badgeId = id;
            if (!_session.Watching && SteamInitializer.Initialized && !SpectatorLocal.IsLocal(id))
                SteamFriends.RequestUserInformation(new CSteamID(id), true);
        }
        _badgeName!.SetTextAutoSize(_session.Watching ? _session.LocalSpectatorName : _session.PlayerName(id));
        if (mapPlayers != null) { _badge!.Show(); return; }
        if (players == null) { _badge!.Hide(); return; }
        float bottom = players.GetChildren().OfType<NMultiplayerPlayerState>()
            .Select(player => player.Position.Y + Math.Max(player.Size.Y, 64)).DefaultIfEmpty(0).Max();
        _badge!.Position = players.Position + new Vector2(0, bottom + 8);
        _badge.Modulate = players.Modulate;
        _badge.Visible = players.IsVisibleInTree() && (NMapScreen.Instance?.IsOpen == true || NRun.Instance?.CombatRoom != null);
    }
}
