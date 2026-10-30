using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Steamworks;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 发现好友并管理观战连接。
// Discover friends and manage spectator connections.

public partial class SpectatorSession
{
    internal string LocalSpectatorName => WatchPlatform == PlatformType.Steam && SteamInitializer.Initialized
        ? SteamFriends.GetPersonaName() : _localName;

    // 查询游戏内好友，由房主响应是否可以观战。
    // Query friends playing this game and ask each host for availability.
    public void RefreshFriends()
    {
        _friends.Clear();
        _discovery = Guid.NewGuid().ToString("N");
        if (SteamInitializer.Initialized)
            for (int i = 0; i < SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate); i++)
            {
                var friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (!SteamFriends.GetFriendGamePlayed(friend, out var info)
                    || info.m_gameID.m_GameID != SteamUtils.GetAppID().m_AppId) continue;
                _friends[friend.m_SteamID] = L.T("friends.no_response");
                Send(friend.m_SteamID, new(2, "query", _discovery));
            }
        FriendsChanged?.Invoke();
    }

    public void Watch(ulong host)
    {
        if (!SteamInitializer.Initialized || RunManager.Instance.IsInProgress) return;
        StopWatching();
        _host = host;
        _token = Guid.NewGuid().ToString("N");
        _lastSequence = 0;
        _received = _heartbeat = _time;
        SetStatus(L.T("status.requesting"));
        Send(host, new(2, "watch", _token, Color: DrawingColor));
    }

    // 本地观战复用相同协议和界面，只替换传输和名字来源。
    // Local spectating shares the protocol and UI while replacing transport and name lookup.
    public bool WatchLocal(string ip, int spectatorPort, string name)
    {
        if (RunManager.Instance.IsInProgress || !SpectatorLocal.ValidAddress(ip, spectatorPort)) return false;
        StopWatching();
        _host = SpectatorLocal.Host;
        _localName = CleanName(name);
        _token = Guid.NewGuid().ToString("N");
        _lastSequence = 0;
        _received = _heartbeat = _time;
        SetStatus(L.T("status.connecting_local"));
        MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(Local.Connect(ip, spectatorPort, () =>
        {
            if (_host == SpectatorLocal.Host) Send(_host, new(2, "watch", _token, Name: _localName, Color: DrawingColor));
        }));
        return true;
    }

    private static string CleanName(string? name)
    {
        string clean = new((name ?? "").Where(ch => !char.IsControl(ch)).Take(40).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? L.T("common.spectator") : clean;
    }
    public string PlayerName(ulong id)
    {
        if (SpectatorLocal.IsLocal(id)) return id == _viewer ? _viewerName : _remoteViewerName;
        var platform = Watching ? WatchPlatform : RunManager.Instance.IsInProgress
            ? RunManager.Instance.NetService.Platform : SteamInitializer.Initialized ? PlatformType.Steam : PlatformType.None;
        return PlatformUtil.GetPlayerNameRaw(platform, id);
    }

    private bool CanContactViewer(ulong id) => SpectatorLocal.IsLocal(id)
        ? IsLocalHost && Local.IsPeer(id) : !IsLocalHost && SteamInitializer.Initialized && IsFriend(id);

    public void StopWatching(string? status = null)
    {
        if (_host != 0) Send(_host, new(2, "leave", _token));
        _host = 0;
        _local?.Disconnect();
        _token = "";
        Map = null;
        ResetInk();
        SetStatus(status ?? L.T("status.left"));
    }

    // 地图可前进且不在旅行或战斗中，才允许首次接入。
    // Admit new viewers only on a travel-ready map outside combat and travel.
    private static bool CanEnter() => NMapScreen.Instance is { IsOpen: true, IsTravelEnabled: true, IsTraveling: false }
        && !CombatManager.Instance.IsInProgress && !CombatManager.Instance.IsStarting
        && NGame.Instance?.Transition.InTransition != true && !RunManager.Instance.NetService.IsGameLoading
        && (RunManager.Instance.DebugOnlyGetState()?.CurrentRoom is not EventRoom
            || RunManager.Instance.EventSynchronizer.Events.All(item => item.IsFinished));

    private static bool IsFriend(ulong id) => SteamFriends.GetFriendRelationship(new CSteamID(id))
        == EFriendRelationship.k_EFriendRelationshipFriend;
    private static bool IsPeer(ulong id) => IsFriend(id) || (RunManager.Instance.IsInProgress
        && RunManager.Instance.DebugOnlyGetState()?.Players.Any(player => player.NetId == id) == true);

    public void KickViewer()
    {
        if (!IsHost || _viewer == 0) return;
        RemoveViewer(L.Wire("status.kicked"));
        ResetInk();
        RelayInk("clear-ink", "");
    }
    private void RemoveViewer(string reason)
    {
        ulong viewer = _viewer;
        _viewer = 0;
        if (viewer == 0) return;
        RelayInk("roster", "0");
        Send(viewer, new(2, "end", _viewerToken, reason));
        if (SpectatorLocal.IsLocal(viewer)) _local?.ClosePeer(viewer);
    }
}
