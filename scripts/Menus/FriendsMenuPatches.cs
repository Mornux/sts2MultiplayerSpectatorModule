using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 接入原版好友列表生命周期。
// Hook the native friend list lifecycle.

public static partial class Entry
{

    // 仅替换观战页面的打开和刷新，普通加入流程保持原生行为。
    // Replace opening and refreshing only for the spectator page.
    [HarmonyPatch(typeof(NJoinFriendScreen), nameof(NJoinFriendScreen.OnSubmenuOpened))]
    private static class FriendsOpenPatch
    {
        private static bool Prefix(NJoinFriendScreen __instance)
            => SpectatorPanel.Instance?.OnFriendsOpened(__instance) != true;
    }

    [HarmonyPatch(typeof(NJoinFriendScreen), "RefreshButtonClicked")]
    private static class FriendsRefreshPatch
    {
        private static bool Prefix(NJoinFriendScreen __instance)
            => SpectatorPanel.Instance?.OnFriendsRefresh(__instance) != true;
    }

    [HarmonyPatch(typeof(NJoinFriendScreen), nameof(NJoinFriendScreen.OnSubmenuClosed))]
    private static class FriendsClosePatch
    {
        private static void Postfix(NJoinFriendScreen __instance) => SpectatorPanel.Instance?.OnFriendsClosed(__instance);
    }
}
