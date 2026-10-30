namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 水晶球只展示三次或六次占卜选项，不采集或显示小游戏棋盘。
// Show only the three- or six-divination choices, without capturing or displaying the minigame board.
internal static class CrystalSphereEvent
{
    internal static EventPlayerState Enrich(EventPlayerState state) => state with
    {
        Cells = null,
        Details = "",
        Options = state.Options.Where(option => option.Key.EndsWith(".UNCOVER_FUTURE", StringComparison.Ordinal)
            || option.Key.EndsWith(".PAYMENT_PLAN", StringComparison.Ordinal)).ToArray()
    };
}
