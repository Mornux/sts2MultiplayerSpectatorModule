using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 接入原版地图绘画操作。
// Hook native map drawing actions.

[HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.BeginLineLocal))]
internal static class SpectatorInkBegin
{
    private static void Postfix(NMapDrawings __instance, Vector2 position, DrawingMode? overrideDrawingMode) =>
        SpectatorMap.Find(__instance)?.BeginStroke(position, overrideDrawingMode);
}

[HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.UpdateCurrentLinePositionLocal))]
internal static class SpectatorInkContinue
{
    private static void Postfix(NMapDrawings __instance, Vector2 position) => SpectatorMap.Find(__instance)?.ExtendStroke(position);
}

[HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.StopLineLocal))]
internal static class SpectatorInkEnd
{
    private static void Postfix(NMapDrawings __instance) => SpectatorMap.Find(__instance)?.EndStroke();
}

[HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.ClearDrawnLinesLocal))]
internal static class SpectatorInkClear
{
    private static void Postfix(NMapDrawings __instance) => SpectatorMap.Find(__instance)?.ClearInk();
}



// 保留每位玩家的绘画颜色。
// Preserve drawing colors for each participant.

[HarmonyPatch(typeof(NMapDrawings), "CreateLineForPlayer")]
internal static class SpectatorPlayerInkColor
{
    private static void Postfix(NMapDrawings __instance, Player player, Line2D __result)
    {
        if (__instance is SpectatorNativeInk spectator) { __result.DefaultColor = spectator.StrokeColor; return; }
        if (SpectatorMap.Find(__instance)?.InkColor(player.NetId) is { } color) __result.DefaultColor = color;
    }
}
