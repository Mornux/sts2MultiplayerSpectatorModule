using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Platform;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 阻止观战操作与进度写入。
// Block spectator actions and progress writes.

[HarmonyPatch]
internal static class SpectatorBattleNoInput
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "StartCardPlay" }) yield return AccessTools.DeclaredMethod(typeof(NPlayerHand), name);
        foreach (string name in new[] { "OnRelease", "CallReleaseLogic", "SecretEndTurnLogicViaFtue" }) yield return AccessTools.DeclaredMethod(typeof(NEndTurnButton), name);
        yield return AccessTools.DeclaredMethod(typeof(ActionQueueSynchronizer), "RequestEnqueue");
        yield return AccessTools.DeclaredMethod(typeof(NRun), "ShowGameOverScreen");
        yield return AccessTools.DeclaredMethod(typeof(NCombatUi), "OnCombatWon");
    }
    private static bool Prefix() => !SpectatorCombat.Watching;
}

[HarmonyPatch]
internal static class SpectatorBattleReadOnlyHand
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { "CanPlayCards" }
        .Select(name => AccessTools.DeclaredMethod(typeof(NPlayerHand), name));
    private static bool Prefix(ref bool __result)
    {
        if (!SpectatorCombat.Watching) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch]
internal static class SpectatorBattleEnd
{
    private static MethodBase TargetMethod() => typeof(CombatManager).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(method => method.Name == "EndCombatInternal" && method.GetParameters().Length == 1);
    private static bool Prefix(object[] __args, ref Task __result)
    {
        if (!SpectatorCombat.Watching) return true;
        SpectatorCombat.Instance!.EndLocally(__args[0]);
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
internal static class SpectatorBattleNoSettlement
{
    private static bool Prefix(ref SerializableRun __result)
    {
        if (!SpectatorCombat.Watching) return true;
        __result = RunManager.Instance.ToSave(null);
        return false;
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
internal static class SpectatorBattleNoSave
{
    private static bool Prefix(ref Task __result)
    {
        if (!SpectatorCombat.Watching) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch]
internal static class SpectatorBattleNoProgress
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { "SaveProgressFile", "UpdateProgressAfterCombatWon", "DeleteCurrentRun", "DeleteCurrentMultiplayerRun", "SaveRunHistory" }
        .Select(name => AccessTools.DeclaredMethod(typeof(SaveManager), name));
    private static bool Prefix() => !SpectatorCombat.Watching;
}

[HarmonyPatch(typeof(AchievementsUtil), nameof(AchievementsUtil.Unlock))]
internal static class SpectatorBattleNoAchievements
{
    private static bool Prefix() => !SpectatorCombat.Watching;
}
