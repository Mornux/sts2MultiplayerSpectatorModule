using System.Reflection;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;

namespace sts2MultiplayerSpectatorModule.Scripts.Treasure;

// Keep native scenery children alive without registering a gameplay room, input callbacks or reward listeners.
[HarmonyPatch]
internal static class SpectatorTreasureLifecycle
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        "_EnterTree", "_Ready", "_ExitTree", "OnChestButtonReleased", "OnProceedButtonPressed",
        "OnProceedButtonReleased", "RefreshVotes", "OnMouseEntered", "OnMouseExited", "OnActiveScreenChanged"
    }.Select(name => AccessTools.DeclaredMethod(typeof(NTreasureRoom), name)
        ?? throw new MissingMethodException(typeof(NTreasureRoom).FullName, name));

    private static bool Prefix(NTreasureRoom __instance) => !SpectatorTreasureView.Owns(__instance);
}

[HarmonyPatch(typeof(NTreasureRoom), "OpenChest")]
internal static class SpectatorTreasureNoRewards
{
    private static bool Prefix(NTreasureRoom __instance, ref Task __result)
    {
        if (!SpectatorTreasureView.Owns(__instance))
        {
            SpectatorTreasure.Instance?.HostOpened();
            return true;
        }
        __result = Task.CompletedTask;
        return false;
    }
}

// The collection's normal controller subscribes to the live synchronizer and eventually grants relics.
// Display instances retain its scene and children; only the isolated visual APIs are driven by the view.
[HarmonyPatch]
internal static class SpectatorTreasureCollectionReadOnly
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        "_Ready", "_ExitTree", "Initialize", "InitializeRelics", "PickRelic", "OnRelicsAwarded",
        "RefreshVotes", "AnimIn", "AnimOut", "SetSelectionEnabled"
    }.Select(name => AccessTools.DeclaredMethod(typeof(NTreasureRoomRelicCollection), name)!);

    private static bool Prefix(NTreasureRoomRelicCollection __instance, MethodBase __originalMethod)
    {
        if (SpectatorTreasureView.Find(__instance) == null) return true;
        if (__originalMethod.Name == "_ExitTree") SpectatorTreasureView.Cancel(__instance);
        return false;
    }
}

[HarmonyPatch]
internal static class SpectatorTreasureCollectionNoSettlement
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    { "AnimateRelicAwards", "RelicPickingBegan", "RelicPickingFinished" }
        .Select(name => AccessTools.DeclaredMethod(typeof(NTreasureRoomRelicCollection), name)!);
    private static bool Prefix(NTreasureRoomRelicCollection __instance, ref Task __result)
    {
        if (SpectatorTreasureView.Find(__instance) == null) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch]
internal static class SpectatorTreasureHandsReadOnly
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        "_EnterTree", "_ExitTree", "Initialize", "_Input", "ProcessGuiFocus", "UpdateHandVisibility",
        "OnInputStateAdded", "OnInputStateRemoved", "OnInputStateChanged", "AddHand", "RemoveHand"
    }.Select(name => AccessTools.DeclaredMethod(typeof(NHandImageCollection), name)!);
    private static bool Prefix(NHandImageCollection __instance, MethodBase __originalMethod)
    {
        if (SpectatorTreasureView.Find(__instance) == null) return true;
        if (__originalMethod.Name == "_ExitTree") SpectatorTreasureView.Cancel(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(NTreasureRoomRelicHolder), "PlayerVotedForRelic")]
internal static class SpectatorTreasureVotes
{
    private static bool Prefix(NTreasureRoomRelicHolder __instance, Player player, ref bool __result)
    {
        if (SpectatorTreasureView.Find(__instance) is not { } view) return true;
        __result = view.VotedFor(player, __instance.Index);
        return false;
    }
}


