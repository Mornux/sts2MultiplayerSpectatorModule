using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Multiplayer.Replay;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 接入原版战斗记录入口。
// Hook native combat recording.

// 钩子只影响观战回放；房主和正式玩家继续原版流程。
// Scope read-only behavior to spectator replays, leaving live participants unchanged.
[HarmonyPatch(typeof(CombatReplayWriter), nameof(CombatReplayWriter.RecordInitialState))]
internal static class SpectatorBattleStart
{
    private static void Postfix(CombatReplayWriter __instance) => SpectatorCombat.Instance?.HostCapture(__instance);
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
internal static class SpectatorBattleReady
{
    private static void Postfix() => SpectatorCombat.Instance?.HostBegin();
}

[HarmonyPatch]
internal static class SpectatorBattleEvents
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { "RecordGameAction", "RecordActionResume", "RecordPlayerChoice" }
        .Select(name => AccessTools.DeclaredMethod(typeof(CombatReplayWriter), name));
    private static void Postfix(CombatReplayWriter __instance) => SpectatorCombat.Instance?.HostEvent(__instance);
}
