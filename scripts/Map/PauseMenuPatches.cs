using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.TopBar;

namespace sts2MultiplayerSpectatorModule.Scripts;

[HarmonyPatch(typeof(NTopBarPauseButton), "OnRelease")]
internal static class SpectatorMapPause
{
    private static bool Prefix(NTopBarPauseButton __instance)
    {
        var owner = SpectatorMap.Find(__instance);
        if (owner == null) return true;
        owner.TogglePause();
        return false;
    }
}

// 原版菜单照常打开设置与百科，只替换会修改参战跑局的生命周期。
// Keep native settings and compendium navigation while isolating participant-run lifecycle effects.
[HarmonyPatch]
internal static class SpectatorMapPauseLifecycle
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string method in new[] { "_Ready", "_ExitTree", "OnSubmenuOpened", "OnSubmenuClosed",
            "OnBackOrResumeButtonPressed", "OnGiveUpButtonPressed", "OnDisconnectButtonPressed", "OnSaveAndQuitButtonPressed" })
            yield return AccessTools.DeclaredMethod(typeof(NPauseMenu), method);
    }
    private static bool Prefix(NPauseMenu __instance, MethodBase __originalMethod)
    {
        if (SpectatorMap.Find(__instance) is not { } owner) return true;
        switch (__originalMethod.Name)
        {
            case "_Ready": owner.BindPauseMenu(__instance); break;
            case "OnSubmenuOpened": NHotkeyManager.Instance?.AddBlockingScreen(__instance); break;
            case "OnSubmenuClosed":
                __instance.GetNode<NBackButton>("%BackButton").Disable();
                __instance.Hide();
                SpectatorMap.Set(__instance, "_lastFocusedControl", null);
                NHotkeyManager.Instance?.RemoveBlockingScreen(__instance);
                break;
            case "_ExitTree": NHotkeyManager.Instance?.RemoveBlockingScreen(__instance); break;
            case "OnBackOrResumeButtonPressed": owner.SetPaused(false); break;
        }
        return false;
    }
}

// 原版说明挂到观战叠层，避免被高层地图遮挡；内容和定位仍由原版生成。
// Mount native tips on the spectator overlay; retain native content and positioning.
[HarmonyPatch]
internal static class SpectatorMapHoverTips
{
    private static MethodBase TargetMethod() => typeof(NHoverTipSet).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(NHoverTipSet.CreateAndShow)
            && method.GetParameters()[1].ParameterType == typeof(IEnumerable<IHoverTip>));
    private static void Postfix(Control owner, NHoverTipSet? __result)
    {
        if (__result != null) SpectatorMap.Find(owner)?.ShowHoverTip(__result);
    }
}

// 观战设置使用自己的原版菜单栈，手柄与焦点不能落到下面的真实跑局。
// Use the spectator's native menu stack for controller focus instead of the underlying real run.
[HarmonyPatch(typeof(ActiveScreenContext), nameof(ActiveScreenContext.GetCurrentScreen))]
internal static class SpectatorMapScreenContext
{
    private static void Postfix(ref IScreenContext? __result)
    {
        if (MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer.Instance?.OpenModal != null) return;
        if (SpectatorMap.Active?.CurrentScreen is { } screen) __result = screen;
    }
}

// 确认弹窗保持原版输入和清理逻辑，只调整观战时的显示层级。
// Preserve native modal input and cleanup, adjusting only spectator layering.
[HarmonyPatch(typeof(NModalContainer), nameof(NModalContainer.Add))]
internal static class SpectatorMapModalOpen
{
    private static void Postfix(NModalContainer __instance, Node modalToCreate)
    {
        if (modalToCreate.GetParent() == __instance) SpectatorMap.Active?.ShowModal(modalToCreate);
    }
}

[HarmonyPatch(typeof(NModalContainer), nameof(NModalContainer.Clear))]
internal static class SpectatorMapModalClose
{
    private static void Prefix(NModalContainer __instance)
    {
        if (__instance.OpenModal is Node modal) SpectatorMap.Find(modal)?.DismissModal();
    }
}
