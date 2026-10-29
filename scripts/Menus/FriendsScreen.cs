using Godot;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 刷新好友列表并发起观战。
// Refresh friends and start spectating.

public partial class SpectatorPanel
{

    public void Open(NMultiplayerSubmenu menu)
    {
        _friendsScreen = CreateScreen("SpectatorFriends");
        Push(menu, _friendsScreen);
    }

    public bool OnFriendsOpened(NJoinFriendScreen screen)
    {
        if (screen != _friendsScreen && screen != _managementScreen && screen != _localScreen) return false;
        screen.GetNode<Control>("%LoadingOverlay").Hide();
        screen.GetNode<Control>("%LoadingIndicator").Hide();
        screen.GetNode<MegaLabel>("TitleLabel").SetTextAutoSize(screen == _managementScreen
            ? L.T("menu.management") : screen == _localScreen ? L.T("menu.local") : L.T("menu.choose_friend"));
        if (screen == _localScreen) { CreateLocalForm(screen); return true; }
        if (screen == _managementScreen) RefreshManagement();
        else OnFriendsRefresh(screen);
        return true;
    }

    // 保留原版省略号动画，等待期间隐藏旧列表并避免重复刷新。
    // Keep native animated dots, hide old results, and prevent overlapping refreshes.
    public bool OnFriendsRefresh(NJoinFriendScreen screen)
    {
        if (screen == _localScreen)
        {
            if (int.TryParse(_localPort!.Text, out int port)
                && _session.WatchLocal(_localAddress!.Text.Trim(), port, _localName!.Text)) _map.Show();
            else _localError!.SetTextAutoSize(L.T("local.invalid_address"));
            return true;
        }
        if (screen == _managementScreen)
        {
            if (_session.IsHost) _session.AllowSpectators = !_session.AllowSpectators;
            RefreshManagement();
            return true;
        }
        if (screen != _friendsScreen) return false;
        if (!_refreshing) TaskHelper.RunSafely(RefreshFriendsAsync(screen));
        return true;
    }

    private async Task RefreshFriendsAsync(NJoinFriendScreen screen)
    {
        _refreshing = true;
        int generation = ++_refreshGeneration;
        screen.GetNode<Control>("%NoFriendsText").Hide();
        screen.GetNode<Control>("%ButtonContainer").Hide();
        screen.GetNode<Control>("%LoadingIndicator").Show();
        _session.RefreshFriends();
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        if (generation != _refreshGeneration || screen != _friendsScreen || !GodotObject.IsInstanceValid(screen)) return;
        _refreshing = false;
        screen.GetNode<Control>("%LoadingIndicator").Hide();
        screen.GetNode<Control>("%ButtonContainer").Show();
        RefreshList();
        screen.GetNode<Control>("%ButtonContainer").GetChildren().OfType<NButton>().FirstOrDefault()?.TryGrabFocus();
    }

    public void OnFriendsClosed(NJoinFriendScreen screen)
    {
        if (screen == _localScreen)
        {
            _session.StopWatching();
            _map.Hide();
            _localScreen = null;
        }
        else if (screen == _managementScreen)
        {
            _managementScreen = null;
            _managementState = "";
        }
        else if (screen == _friendsScreen)
        {
            _session.StopWatching();
            _map.Hide();
            _friendsScreen = null;
            _refreshing = false;
            _refreshGeneration++;
        }
        else return;
        screen.QueueFree();
    }

    private void RefreshList()
    {
        if (_friendsScreen == null || _refreshing) return;
        // Steam 使用好友列表，本地模式才提供地址连接入口。
        // Use the friend list on Steam and offer address-based connections only in local mode.
        bool local = !SteamInitializer.Initialized;
        var container = ClearList(_friendsScreen, L.T("friends.none"), !local && _session.Friends.Count == 0);
        foreach (var (id, status) in _session.Friends)
            AddFriend(container, id, status, () => { _session.Watch(id); _map.Show(); });
        if (local) AddFriend(container, 0, L.T("local.enter_address"), () =>
        {
            _localScreen = CreateScreen("SpectatorLocal");
            Push(_friendsScreen, _localScreen);
        }, L.T("local.connect"));
        ActiveScreenContext.Instance.Update();
    }
}
