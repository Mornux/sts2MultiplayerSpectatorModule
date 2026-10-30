using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 模组详情直接读取内嵌封面，保持只导出 DLL 和清单的结构。
// Load the embedded cover in mod details while retaining the DLL-and-manifest export.
[HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer.Fill))]
internal static class ModImagePatch
{
    private static Texture2D? _texture;
    private static void Postfix(NModInfoContainer __instance, Mod mod)
    {
        if (mod.manifest?.id != "sts2MultiplayerSpectatorModule") return;
        if (_texture == null)
        {
            using var data = ModResources.Open("mod_image.png");
            if (data == null) return;
            using var buffer = new MemoryStream();
            data.CopyTo(buffer);
            using var image = new Image();
            if (image.LoadPngFromBuffer(buffer.ToArray()) != Error.Ok) return;
            _texture = ImageTexture.CreateFromImage(image);
        }
        __instance.GetNode<TextureRect>("ModImage").Texture = _texture;
    }
}
