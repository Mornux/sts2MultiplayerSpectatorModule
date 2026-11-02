using MegaCrit.Sts2.Core.Localization;
using Godot;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 注册入口和常驻观战节点。
// Register the entry point and persistent spectator node.
[ModInitializer(nameof(Init))]
public static partial class Entry
{
    public static void Init()
    {
        L.Language = () => LocManager.Instance?.Language ?? "eng";
        ScriptManagerBridge.LookupScriptsInAssembly(typeof(Entry).Assembly);
        var harmony = new Harmony("mornux.sts2.spectator");
        try { harmony.PatchAll(typeof(Entry).Assembly); }
        catch
        {
            // A failed install must not leave half of the read-only guards active.
            harmony.UnpatchAll(harmony.Id);
            throw;
        }
        if (NGame.Instance is { } game && game.IsNodeReady())
            game.CallDeferred(Node.MethodName.AddChild, new SpectatorPanel());
    }

    [HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
    private static class GameReadyPatch
    {
        private static void Postfix(NGame __instance)
        {
            if (SpectatorPanel.Instance == null)
                __instance.AddChild(new SpectatorPanel());
        }
    }
}
