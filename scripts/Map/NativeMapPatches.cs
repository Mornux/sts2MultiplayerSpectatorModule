using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.sts2.Core.Nodes.TopBar;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 隔离原版地图的观战行为。
// Isolate spectator behavior in the native map.

// 补丁仅作用于观战子节点，保留正常单人和多人地图的行为。
// Scope patches to spectator descendants, preserving normal game maps.
[HarmonyPatch]
internal static class SpectatorMapLifecycle
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string method in new[] { "_Ready", "_Notification", "_Input", "InitMapVotes", "RefreshAllMapPointVotes", "RecalculateTravelability" })
            yield return AccessTools.DeclaredMethod(typeof(NMapScreen), method);
        foreach (string method in new[] { "_Ready", "_EnterTree", "_ExitTree", "_Notification", "_Input" })
            yield return AccessTools.DeclaredMethod(typeof(NTopBar), method);
        yield return AccessTools.DeclaredMethod(typeof(NRunTimer), "_Ready");
        yield return AccessTools.DeclaredMethod(typeof(NRunTimer), "_Notification");
        yield return AccessTools.DeclaredMethod(typeof(NTopBarDeckButton), "_Notification");
    }
    private static bool Prefix(Node __instance, MethodBase __originalMethod)
    {
        if (__originalMethod.Name == "_Notification" && __instance.HasMeta("spectator_view")) return false;
        var owner = SpectatorMap.Find(__instance);
        if (owner == null) return true;
        if (__instance is NMapScreen map && __originalMethod.Name == "_Ready") owner.BindNativeMap(map);
        return false;
    }
}

[HarmonyPatch(typeof(NMapScreen), "DrawPaths")]
internal static class SpectatorMapPositions
{
    private static void Prefix(NMapScreen __instance) => SpectatorMap.Find(__instance)?.RestorePositions();
}

// 释放节点仅播放音效，阻止选路、投票和进入房间。
// Node release only plays sound, preventing route selection, votes, and room entry.
[HarmonyPatch(typeof(NMapPoint), "OnRelease")]
internal static class SpectatorMapClick
{
    private static bool Prefix(NMapPoint __instance)
    {
        var owner = SpectatorMap.Find(__instance);
        if (owner == null) return true;
        owner.ClickNode();
        return false;
    }
}

// 楼层直接显示房主快照值，不读取用于展示的空历史记录。
// Display the host's floor rather than the display model's empty history.
[HarmonyPatch(typeof(NTopBarFloorIcon), "UpdateIcon")]
internal static class SpectatorMapFloor
{
    private static bool Prefix(NTopBarFloorIcon __instance)
    {
        var owner = SpectatorMap.Find(__instance);
        if (owner == null) return true;
        if (owner.Snapshot is { } snapshot)
            __instance.GetNode<MegaLabel>("FloorNumLabel").SetTextAutoSize(snapshot.Floor.ToString());
        return false;
    }
}

// 只在原版节点和图例的同步调用期间，将静态地图指向观战实例。
// Redirect static map lookup only during synchronous spectator node and legend calls.
[HarmonyPatch]
internal static class SpectatorMapContext
{
    [ThreadStatic] internal static NMapScreen? Current;
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string method in new[] { "_EnterTree", "_ExitTree" }) yield return AccessTools.Method(typeof(NNormalMapPoint), method);
        foreach (string method in new[] { "OnFocus", "OnUnfocus" }) yield return AccessTools.Method(typeof(NMapLegendItem), method);
    }
    private static void Prefix(Node __instance, out NMapScreen? __state)
    {
        __state = Current;
        if (SpectatorMap.Find(__instance) == null) return;
        for (Node? parent = __instance; parent != null; parent = parent.GetParent())
            if (parent is NMapScreen map) { Current = map; break; }
    }
    private static void Finalizer(NMapScreen? __state) => Current = __state;
}

[HarmonyPatch(typeof(NMapScreen), "get_Instance")]
internal static class SpectatorMapInstance
{
    private static bool Prefix(ref NMapScreen? __result)
    {
        if (SpectatorMapContext.Current == null) return true;
        __result = SpectatorMapContext.Current;
        return false;
    }
}
