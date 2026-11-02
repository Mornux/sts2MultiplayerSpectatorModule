using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Multiplayer.Replay;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 接入原版战斗记录入口。
// Hook native combat recording.

// 回调只采集已完成的原版记录；采集函数自行隔离异常，正式玩家继续原版流程。
// Observe completed native records; capture callbacks contain their failures and leave native play unchanged.
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
        .Select(name => AccessTools.DeclaredMethod(typeof(CombatReplayWriter), name)
            ?? throw new MissingMethodException(typeof(CombatReplayWriter).FullName, name));
    private static void Postfix(CombatReplayWriter __instance) => SpectatorCombat.Instance?.HostEvent(__instance);
}
