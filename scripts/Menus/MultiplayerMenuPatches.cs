using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 创建原版风格的观战入口。
// Create the native-style spectator entry.

public static partial class Entry
{

    // 复用原生加入卡片的场景、图片、材质和交互。
    // Reuse the native join card scene, artwork, material, and interactions.
    [HarmonyPatch(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu._Ready))]
    private static class MenuPatch
    {
        private static void Postfix(NMultiplayerSubmenu __instance)
        {
            var container = __instance.GetNode<HBoxContainer>("ButtonContainer");
            var join = container.GetNode<NSubmenuButton>("JoinButton");
            var button = ResourceLoader.Load<PackedScene>("res://scenes/ui/submenu_button.tscn").Instantiate<NSubmenuButton>();
            button.Name = "SpectatorButton";
            button.CustomMinimumSize = join.CustomMinimumSize;
            button.SizeFlagsHorizontal = join.SizeFlagsHorizontal;
            button.SizeFlagsVertical = join.SizeFlagsVertical;
            // 从 DLL 内嵌资源读取卡图，不依赖外部图片目录。
            // Read the portrait embedded in the DLL without a loose image directory.
            using var portraitData = ModResources.Open("pictures.spectator.png");
            var portrait = new Image();
            if (portraitData != null)
            {
                using var buffer = new System.IO.MemoryStream();
                portraitData.CopyTo(buffer);
                portrait.LoadPngFromBuffer(buffer.ToArray());
            }
            // 原版边框画在图片内，保留边缘并将新卡图嵌入框内。
            // Keep the native image's baked-in border and inset the new portrait.
            var icon = button.GetNode<TextureRect>("Icon");
            icon.Texture = join.GetNode<TextureRect>("Icon").Texture;
            if (portrait is { } && !portrait.IsEmpty())
            {
                var picture = new TextureRect
                {
                    Texture = ImageTexture.CreateFromImage(portrait),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                icon.AddChild(picture);
                picture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                picture.AnchorLeft = 8f / icon.Texture.GetWidth();
                picture.AnchorRight = 1f - picture.AnchorLeft;
                picture.AnchorTop = 8f / icon.Texture.GetHeight();
                picture.AnchorBottom = 1f - picture.AnchorTop;
            }
            // 使用 RGB 基色保留纸张纹理和原生悬停亮度。
            // Apply an RGB base color while retaining paper texture and native hover brightness.
            var material = new ShaderMaterial
            {
                Shader = new Shader
                {
                    Code = """
                        shader_type canvas_item;
                        uniform vec4 tint : source_color = vec4(1.0);
                        uniform float s = 1.0;
                        uniform float v = 0.7;
                        varying vec4 modulation;
                        void vertex() { modulation = COLOR; }
                        void fragment() {
                            vec4 paper = texture(TEXTURE, UV);
                            float grain = dot(paper.rgb, vec3(0.299, 0.587, 0.114));
                            vec3 color = tint.rgb * grain;
                            float gray = dot(color, vec3(0.299, 0.587, 0.114));
                            color = mix(vec3(gray), color, s) * (v / 0.7);
                            COLOR = vec4(color, paper.a * tint.a) * modulation;
                        }
                        """
                }
            };
            material.SetShaderParameter("tint", new Color(237f / 255f, 200f / 255f, 125f / 255f));
            // 原生按钮会读取亮度并在移出鼠标时恢复，必须显式初始化。
            // Initialize brightness explicitly for the native button's hover restoration.
            material.SetShaderParameter("s", 1f);
            material.SetShaderParameter("v", 0.7f);
            button.GetNode<Control>("BgPanel").Material = material;
            container.AddChild(button);
            button.SetIconAndLocalization("JOIN");
            button.Connect(NClickableControl.SignalName.Released,
                Callable.From<NButton>(_ => SpectatorPanel.Instance?.Open(__instance)));
            FitCards(__instance);
        }
    }

    // 保持原生字体，并为有存档时的四张卡片留出空间。
    // Keep native typography and fit all four cards when a saved run exists.
    [HarmonyPatch(typeof(NSubmenuButton), nameof(NSubmenuButton.RefreshLabels))]
    private static class CardLabelsPatch
    {
        private static void Postfix(NSubmenuButton __instance)
        {
            if (__instance.Name != "SpectatorButton") return;
            __instance.GetNode<MegaLabel>("%Title").SetTextAutoSize(L.T("menu.spectate"));
            __instance.GetNode<MegaRichTextLabel>("%Description").Text = L.T("menu.description");
        }
    }

    private static void FitCards(NMultiplayerSubmenu menu)
    {
        var container = menu.GetNode<HBoxContainer>("ButtonContainer");
        if (!container.HasNode("SpectatorButton")) return;
        int count = container.GetChildren().OfType<Control>().Count(control => control.Visible);
        int separation = count > 3 ? 40 : 80;
        container.AddThemeConstantOverride("separation", separation);
        float width = count * 330 + (count - 1) * separation;
        container.OffsetLeft = -width / 2;
        container.OffsetRight = width / 2;
    }

    [HarmonyPatch(typeof(NMultiplayerSubmenu), "UpdateButtons")]
    private static class LayoutPatch
    {
        private static void Postfix(NMultiplayerSubmenu __instance) => FitCards(__instance);
    }
}
