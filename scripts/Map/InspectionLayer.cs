using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.InspectScreens;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 检视窗口保持原版实例及操作，仅提升显示层级。
// Retain native inspection instances and behavior while raising their canvas layer.
[HarmonyPatch]
internal static class SpectatorInspectLayer
{
    private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NGame), nameof(NGame.GetInspectCardScreen));
        yield return AccessTools.Method(typeof(NGame), nameof(NGame.GetInspectRelicScreen));
    }
    private static void Postfix(Control __result)
    {
        if (SpectatorMap.Active == null && !SpectatorCombat.Watching || __result.GetParent() is CanvasLayer) return;
        var layer = new CanvasLayer { Layer = 190 };
        NGame.Instance!.AddChild(layer);
        __result.Reparent(layer, false);
    }
}
