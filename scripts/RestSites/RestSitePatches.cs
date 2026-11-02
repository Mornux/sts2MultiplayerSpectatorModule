using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace sts2MultiplayerSpectatorModule.Scripts;

// Keep native presentation nodes; none of the display room's lifecycle may enter the live synchronizer.
[HarmonyPatch]
internal static class SpectatorRestSiteLifecycle
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        "_Ready", "_EnterTree", "_ExitTree", "_Input", "UpdateRestSiteOptions", "ShowProceedButton",
        "OnProceedButtonReleased", "AfterSelectingOption", "ExtinguishFireIfAble", "OnActiveScreenUpdated"
    }.Select(name => AccessTools.DeclaredMethod(typeof(NRestSiteRoom), name)
        ?? throw new MissingMethodException(typeof(NRestSiteRoom).FullName, name));

    private static bool Prefix(NRestSiteRoom __instance, MethodBase __originalMethod)
    {
        if (SpectatorRestSiteView.Find(__instance) is not { } view) return true;
        if (__originalMethod.Name == "_Ready") view.BindRoom(__instance);
        return false;
    }
}

// Even programmatic/native controller activation cannot choose an option in a spectator-owned room.
[HarmonyPatch]
internal static class SpectatorRestSiteReadOnlyButton
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { "OnRelease", "RefreshTextState" }
        .Select(name => AccessTools.DeclaredMethod(typeof(NRestSiteButton), name)
            ?? throw new MissingMethodException(typeof(NRestSiteButton).FullName, name));

    private static bool Prefix(NRestSiteButton __instance) => SpectatorRestSiteView.Find(__instance) == null;
}

[HarmonyPatch(typeof(NRestSiteButton), "SelectOption")]
internal static class SpectatorRestSiteNoSelection
{
    private static bool Prefix(NRestSiteButton __instance, ref Task __result)
    {
        if (SpectatorRestSiteView.Find(__instance) == null) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch]
internal static class SpectatorRestSiteNoTargeting
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { "OnFocus", "OnUnfocus", "Deselect" }
        .Select(name => AccessTools.DeclaredMethod(typeof(NRestSiteCharacter), name)
            ?? throw new MissingMethodException(typeof(NRestSiteCharacter).FullName, name));
    private static bool Prefix(NRestSiteCharacter __instance) => SpectatorRestSiteView.Find(__instance) == null;
}
