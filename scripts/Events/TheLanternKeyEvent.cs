namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 灯笼钥匙显示归还或保留的分支，战斗及奖励阶段仍回地图。
// Show the Lantern Key branches and return to the map for combat and rewards.
internal static class TheLanternKeyEvent
{
    internal static EventPlayerState Enrich(EventPlayerState state) => state with
    { Details = L.Wire("events.lantern_key") };
}
