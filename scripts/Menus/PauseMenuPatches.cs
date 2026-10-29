using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;

namespace sts2MultiplayerSpectatorModule.Scripts;

public static partial class Entry
{

    // 在原生暂停菜单加入房主专用的观战管理按钮。
    // Add a host-only spectator management button to the native pause menu.
    [HarmonyPatch(typeof(NPauseMenu), nameof(NPauseMenu._Ready))]
    private static class PauseMenuPatch
    {
        private static void Postfix(NPauseMenu __instance)
        {
            if (SpectatorMap.Find(__instance) != null) return;
            var container = __instance.GetNode<Control>("%ButtonContainer");
            var button = ResourceLoader.Load<PackedScene>("res://scenes/pause_menu/pause_menu_button.tscn")
                .Instantiate<NPauseMenuButton>();
            button.Name = "SpectatorManagement";
            container.AddChild(button);
            container.MoveChild(button, 1);
            button.GetNode<MegaLabel>("Label").SetTextAutoSize(L.T("menu.management"));
            button.Visible = SpectatorPanel.Instance?.IsHost == true;
            button.Connect(NClickableControl.SignalName.Released,
                Callable.From<NButton>(_ => SpectatorPanel.Instance?.OpenManagement(__instance)));
            __instance.VisibilityChanged += () =>
            {
                button.Visible = SpectatorPanel.Instance?.IsHost == true;
                var visible = container.GetChildren().OfType<NButton>().Where(item => item.Visible).ToArray();
                for (int i = 0; i < visible.Length; i++)
                {
                    visible[i].FocusNeighborTop = visible[(i + visible.Length - 1) % visible.Length].GetPath();
                    visible[i].FocusNeighborBottom = visible[(i + 1) % visible.Length].GetPath();
                }
            };
        }
    }
}
