using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using static sts2MultiplayerSpectatorModule.Scripts.SpectatorMap;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 适配观战回放的原版界面。
// Adapt native UI during spectator playback.

[HarmonyPatch(typeof(NTopBarMapButton), "OnRelease")]
internal static class SpectatorBattleMap
{
    private static bool Prefix()
    {
        if (!SpectatorCombat.Watching) return true;
        SpectatorCombat.Instance!.ToggleMap();
        return false;
    }
}

[HarmonyPatch(typeof(NTopBarPauseButton), "OnRelease")]
internal static class SpectatorBattleMenu
{
    private static bool Prefix(NTopBarPauseButton __instance)
    {
        if (!SpectatorCombat.Watching || Find(__instance) != null) return true;
        SpectatorCombat.Instance!.OpenMenu();
        return false;
    }
}

[HarmonyPatch]
internal static class SpectatorBattleCatchUp
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(Cmd).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(method => method.Name == "Wait" && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(float));
    private static void Prefix(object[] __args)
    {
        if (SpectatorCombat.Watching && SpectatorCombat.Instance!.CatchingUp) __args[0] = (float)__args[0] * 0.5f;
    }
}

[HarmonyPatch(typeof(NEndTurnButton), nameof(NEndTurnButton.RefreshEnabled))]
internal static class SpectatorBattleDisabledEndTurn
{
    private static void Postfix(NEndTurnButton __instance)
    {
        if (SpectatorCombat.Watching) __instance.Disable();
    }
}

[HarmonyPatch(typeof(NRunTimer), "OnTimerTimeout")]
internal static class SpectatorBattleTimer
{
    private static bool Prefix()
    {
        if (!SpectatorCombat.Watching) return true;
        SpectatorCombat.Instance!.UpdateTimer();
        return false;
    }
}

[HarmonyPatch(typeof(CombatReplayWriter), "RecordChecksum")]
internal static class SpectatorBattleChecksum
{
    private static void Postfix(NetChecksumData checksum) => SpectatorCombat.Instance?.Checksum(checksum);
}
