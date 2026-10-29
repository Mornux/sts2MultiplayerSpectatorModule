using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 输入本地观战连接信息。
// Enter local spectator connection details.

public partial class SpectatorPanel
{
    private LineEdit? _localAddress, _localPort, _localName;
    private MegaLabel? _localError;

    // 继续使用原版面板、字体和开始按钮，只增加地址、端口和观战名输入。
    // Keep the native panel, fonts, and action button with address, port, and spectator name inputs.
    private void CreateLocalForm(NJoinFriendScreen screen)
    {
        screen.GetNode<Control>("%ButtonContainer").Hide();
        screen.GetNode<Control>("%NoFriendsText").Hide();
        screen.GetNode<MegaLabel>("%RefreshButton/Label").SetTextAutoSize(L.T("menu.start"));
        var form = new VBoxContainer { Name = "LocalForm" };
        screen.GetNode<Control>("Panel").AddChild(form);
        form.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        form.Position = new Vector2(60, 75);
        form.Size = new Vector2(710, 520);
        form.AddThemeConstantOverride("separation", 16);
        var template = screen.GetNode<MegaLabel>("%NoFriendsText");
        MegaLabel Caption(string text)
        {
            var label = (MegaLabel)template.Duplicate();
            label.UniqueNameInOwner = false;
            label.Visible = true;
            label.CustomMinimumSize = new Vector2(0, 42);
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.AddThemeFontSizeOverride("font_size", 30);
            label.Text = text;
            form.AddChild(label);
            return label;
        }
        LineEdit Input(string value, int maxLength)
        {
            var input = new LineEdit { Text = value, MaxLength = maxLength, CustomMinimumSize = new Vector2(0, 54) };
            input.AddThemeFontOverride("font", template.GetThemeFont("font"));
            input.AddThemeFontSizeOverride("font_size", 30);
            form.AddChild(input);
            return input;
        }
        Caption(L.T("local.address_label"));
        _localAddress = Input("127.0.0.1", 64);
        Caption(L.T("local.port_label"));
        _localPort = Input("33771", 5);
        Caption(L.T("local.name_label"));
        _localName = Input(L.T("common.spectator"), 40);
        _localError = Caption("");
        _localAddress.GrabFocus();
    }
}
