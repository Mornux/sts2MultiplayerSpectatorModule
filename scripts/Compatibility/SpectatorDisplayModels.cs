using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace sts2MultiplayerSpectatorModule.Scripts;

// Native player construction discovers its starting cards/relics. Display copies must not do that.
internal static class SpectatorDisplayModels
{
    [ThreadStatic] private static int _depth;
    internal static bool Creating => _depth > 0;

    internal static Player Create(CharacterModel character, ulong id)
    {
        _depth++;
        try { return Player.CreateForNewRun(character, UnlockState.all, id); }
        finally { _depth--; }
    }
}

[HarmonyPatch]
internal static class SpectatorDisplayDiscovery
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { "MarkCardAsSeen", "MarkRelicAsSeen" }
        .Select(name => AccessTools.DeclaredMethod(typeof(SaveManager), name)
            ?? throw new MissingMethodException(typeof(SaveManager).FullName, name));
    private static bool Prefix() => !SpectatorDisplayModels.Creating;
}
