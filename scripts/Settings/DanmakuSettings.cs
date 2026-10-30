using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 显示方式仅保存在本机，不改变消息发送和其他玩家的设置。
// Persist presentation locally without changing message delivery or other players' preferences.
internal static class DanmakuSettings
{
    private const string ConfigPath = "user://spectator-chat.cfg";
    private static bool? _scrolling;
    internal static bool Scrolling
    {
        get
        {
            if (_scrolling == null)
            {
                var config = new ConfigFile();
                _scrolling = config.Load(ConfigPath) != Error.Ok || config.GetValue("chat", "scrolling", true).AsBool();
            }
            return _scrolling.Value;
        }
        set
        {
            _scrolling = value;
            var config = new ConfigFile();
            config.SetValue("chat", "scrolling", value);
            config.Save(ConfigPath);
        }
    }

    // 所有客户端都可以在原版常规设置中切换弹幕显示。
    // Offer chat display preferences in native general settings for every client.
    internal static void Add(NSettingsScreen screen)
    {
        var panel = screen.GetNode<NSettingsPanel>("%GeneralSettings");
        var content = panel.Content;
        if (content.HasNode("SpectatorChatDisplay")) return;
        var row = new HBoxContainer { Name = "SpectatorChatDisplay", CustomMinimumSize = new Vector2(0, 64) };
        var caption = (MegaRichTextLabel)content.GetNode<MegaRichTextLabel>("ShowMpDrawings/Label").Duplicate();
        caption.Text = L.T("settings.chat_display");
        caption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        caption.CustomMinimumSize = new Vector2(330, 48);
        row.AddChild(caption);
        var button = ResourceLoader.Load<PackedScene>("res://scenes/pause_menu/pause_menu_button.tscn").Instantiate<NPauseMenuButton>();
        button.Name = "Mode"; button.CustomMinimumSize = new Vector2(330, 48);
        row.AddChild(button);
        content.AddChild(row); content.MoveChild(row, 0);
        void Refresh() => button.GetNode<MegaLabel>("Label").SetTextAutoSize(L.T(Scrolling ? "settings.chat_scroll" : "settings.chat_log"));
        Refresh();
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => { Scrolling = !Scrolling; Refresh(); }));
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(panel) && panel.IsInsideTree()) SpectatorMap.Call(panel, "RefreshSize");
        }).CallDeferred();
    }
}
