using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using static sts2MultiplayerSpectatorModule.Scripts.SpectatorSession;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 设置并保存观战绘画颜色。
// Configure and persist spectator drawing colors.

public partial class SpectatorMap
{

    // 记住本机观战颜色；只有合法的三个 RGB 数值才会保存和同步。
    // Persist the local spectator color and synchronize only valid RGB values.
    private const string ColorConfigPath = "user://spectator-color.cfg";
    private static string LoadDrawingColor()
    {
        var config = new ConfigFile();
        string color = config.Load(ColorConfigPath) == Error.Ok ? config.GetValue("drawing", "rgb", "dca747").AsString() : "dca747";
        return ValidColor(color) ? color : "dca747";
    }

    internal void AddColorSettings(NSettingsScreen screen)
    {
        var panel = screen.GetNode<NSettingsPanel>("%GeneralSettings");
        var content = panel.Content;
        if (content.HasNode("SpectatorDrawingColor")) return;
        var row = new HBoxContainer { Name = "SpectatorDrawingColor", CustomMinimumSize = new Vector2(0, 64) };
        row.AddThemeConstantOverride("separation", 10);
        var caption = (MegaRichTextLabel)content.GetNode<MegaRichTextLabel>("ShowMpDrawings/Label").Duplicate();
        caption.Text = L.T("settings.color");
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        caption.CustomMinimumSize = new Vector2(330, 48);
        row.AddChild(caption);
        var inputs = new LineEdit[3];
        var color = new Color(_session.DrawingColor);
        int[] rgb = [(int)Math.Round(color.R * 255), (int)Math.Round(color.G * 255), (int)Math.Round(color.B * 255)];
        for (int index = 0; index < inputs.Length; index++)
        {
            var label = new Label { Text = "RGB"[index].ToString(), VerticalAlignment = VerticalAlignment.Center };
            label.AddThemeFontOverride("font", caption.GetThemeFont("normal_font"));
            label.AddThemeFontSizeOverride("font_size", 24);
            row.AddChild(label);
            var input = inputs[index] = new LineEdit { Name = "RGB"[index].ToString(), Text = rgb[index].ToString(),
                MaxLength = 3, CustomMinimumSize = new Vector2(85, 48), SizeFlagsVertical = SizeFlags.ShrinkCenter,
                TooltipText = L.T("settings.rgb_hint") };
            input.AddThemeFontOverride("font", caption.GetThemeFont("normal_font"));
            input.AddThemeFontSizeOverride("font_size", 26);
            row.AddChild(input);
        }
        var swatch = new PanelContainer { Name = "Preview", CustomMinimumSize = new Vector2(42, 42), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var border = new StyleBoxFlat { BgColor = color, BorderColor = Colors.White,
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2 };
        swatch.AddThemeStyleboxOverride("panel", border);
        row.AddChild(swatch);
        foreach (var input in inputs) input.TextChanged += _ =>
        {
            bool valid = true;
            for (int index = 0; index < inputs.Length; index++)
            {
                bool channelValid = int.TryParse(inputs[index].Text, out rgb[index]) && rgb[index] is >= 0 and <= 255;
                inputs[index].AddThemeColorOverride("font_color", channelValid ? Colors.White : Colors.Red);
                valid &= channelValid;
            }
            if (!valid) return;
            string value = $"{rgb[0]:x2}{rgb[1]:x2}{rgb[2]:x2}";
            border.BgColor = new Color(value);
            _session.SetDrawingColor(value);
            RefreshInk(true);
            var config = new ConfigFile();
            config.SetValue("drawing", "rgb", value);
            config.Save(ColorConfigPath);
        };
        content.AddChild(row);
        content.MoveChild(row, 0);
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(panel) && panel.IsInsideTree()) Call(panel, "RefreshSize");
        }).CallDeferred();
    }
}

// 原版设置页只在观战菜单栈内增加颜色设置。
// Extend native settings only when opened inside the spectator menu stack.
[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen.OnSubmenuOpened))]
internal static class SpectatorColorSettings
{
    private static void Postfix(NSettingsScreen __instance)
    {
        DanmakuSettings.Add(__instance);
        SpectatorMap.Find(__instance)?.AddColorSettings(__instance);
    }
}
