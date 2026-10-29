using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 处理弹幕文字输入。
// Handle danmaku text input.

public partial class SpectatorDanmaku
{
    private Key _consumedKey;

    public override void _Input(InputEvent input)
    {
        if (_session == null || input is not InputEventKey key) return;
        if (!key.Pressed && key.Keycode == _consumedKey)
        { _consumedKey = Key.None; GetViewport().SetInputAsHandled(); return; }
        bool enter = key.Keycode is Key.Enter or Key.KpEnter;
        if (!key.Pressed || key.Echo || !enter && key.Keycode != Key.Escape) return;
        if (_editor.Visible)
        {
            if (enter && DisplayServer.ImeGetText().Length > 0) return;
            _consumedKey = key.Keycode;
            GetViewport().SetInputAsHandled();
            if (!enter || string.IsNullOrWhiteSpace(_input.Text)) CloseEditor();
            else if (_session.SendDanmaku(_input.Text)) CloseEditor();
            else _hint.Text = L.T("chat.send_failed");
        }
        else if (enter && _session.CanSendDanmaku && NModalContainer.Instance?.OpenModal == null
            && GetViewport().GuiGetFocusOwner() is not (LineEdit or TextEdit))
        {
            _consumedKey = key.Keycode;
            _editor.Show();
            IsEditing = true;
            RefreshLog();
            _input.Clear();
            _hint.Text = L.T("chat.help");
            NHotkeyManager.Instance?.AddBlockingScreen(this);
            _input.GrabFocus();
            GetViewport().SetInputAsHandled();
        }
    }

    private void CloseEditor()
    {
        if (!_editor.Visible) return;
        _input.ReleaseFocus();
        _editor.Hide();
        IsEditing = false;
        RefreshLog();
        NHotkeyManager.Instance?.RemoveBlockingScreen(this);
    }
}
