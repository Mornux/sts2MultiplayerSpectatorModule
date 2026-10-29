using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Random;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 水晶球只同步已揭开的格子；未揭开区域不泄露道具位置。
// Relay only revealed Crystal Sphere cells, preserving hidden item positions.
[HarmonyPatch(typeof(CrystalSphereMinigame), MethodType.Constructor, new[] { typeof(Player), typeof(Rng), typeof(int) })]
internal static class CrystalSphereEvent
{
    private static readonly Dictionary<ulong, (WeakReference<Player> Owner, WeakReference<CrystalSphereMinigame> Game)> Games = new();
    private static void Postfix(CrystalSphereMinigame __instance, Player owner)
    {
        if (Games.Count > 8) Games.Clear();
        Games[owner.NetId] = (new(owner), new(__instance));
    }
    internal static EventPlayerState Enrich(CrystalSphere model, EventPlayerState state, bool local)
    {
        if (!local || !Games.TryGetValue(state.Id, out var cached) || !cached.Owner.TryGetTarget(out var owner)
            || !ReferenceEquals(owner, model.Owner) || !cached.Game.TryGetTarget(out var game))
            return state with { Details = L.Wire("events.sphere_wait") };
        var rows = Enumerable.Range(0, 11).Select(y => new string(Enumerable.Range(0, 11).Select(x =>
            game.cells[x, y].IsHidden ? '#' : game.cells[x, y].Item != null ? '+' : '.').ToArray())).ToArray();
        return state with { Cells = rows, Details = L.Wire("events.sphere", game.DivinationCount) };
    }
}
