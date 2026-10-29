using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.TopBar;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 复用暂停菜单、提示与弹窗。
// Reuse pause menus, tooltips and modals.

public partial class SpectatorMap
{
    private Control? _pause;
    private NRunSubmenuStack? _menuStack;
    private NPauseMenu? _pauseMenu;
    private Control _hoverTips = null!;
    private Control? _modalOverlay;
    private bool _changingPause;

    // 直接使用原版暂停菜单和设置菜单栈，观战退出按钮放在最底下。
    // Use the native pause menu and settings stack, with spectator exit at the bottom.
    private void CreatePauseMenu()
    {
        if (_pause != null) { MoveChild(_pause, -1); return; }
        _pause = new Control();
        AddChild(_pause);
        _pause.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var backstop = new ColorRect { Color = new Color(0, 0, 0, 0.7f), MouseFilter = MouseFilterEnum.Ignore };
        _pause.AddChild(backstop);
        backstop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var template = ResourceLoader.Load<PackedScene>("res://scenes/screens/capstone_submenu_stack.tscn").Instantiate<Control>();
        _menuStack = template.GetNode<NRunSubmenuStack>("%Submenus");
        template.RemoveChild(_menuStack);
        _pause.AddChild(_menuStack);
        template.Free();
        _pauseMenu = _menuStack.GetSubmenuType<NPauseMenu>();
        _menuStack.Connect(NSubmenuStack.SignalName.StackModified, Callable.From(() =>
        {
            if (!_changingPause && _menuStack.Peek() == null) SetPaused(false);
        }));
        _pause.Hide();
        _pause.ProcessMode = ProcessModeEnum.Disabled;
    }

    internal void BindPauseMenu(NPauseMenu menu)
    {
        Call(menu, "ConnectSignals");
        var buttons = menu.GetNode<Control>("%ButtonContainer");
        Set(menu, "_buttonContainer", buttons);
        foreach (var binding in new[] { ("_resumeButton", "Resume"), ("_settingsButton", "Settings"),
            ("_compendiumButton", "Compendium"), ("_giveUpButton", "GiveUp"),
            ("_disconnectButton", "Disconnect"), ("_saveAndQuitButton", "SaveAndQuit") })
            Set(menu, binding.Item1, buttons.GetNode<NPauseMenuButton>(binding.Item2));
        Set(menu, "_backButton", menu.GetNode<NBackButton>("%BackButton"));
        Set(menu, "_pausedLabel", menu.GetNode<MegaLabel>("%PausedText/Label"));
        Set(menu, "_runState", _displayRun);
        Call(menu, "RefreshLabels");
        buttons.GetNode<NButton>("Resume").Connect(NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => SetPaused(false)));
        foreach (string name in new[] { "Settings", "Compendium" })
            buttons.GetNode<NButton>(name).Connect(NClickableControl.SignalName.Released,
                Callable.From<NButton>(button => Call(menu, "On" + name + "ButtonPressed", button)));
        foreach (string name in new[] { "GiveUp", "Disconnect", "SaveAndQuit" })
        { var button = buttons.GetNode<NButton>(name); button.Hide(); button.Disable(); }
        var exit = ResourceLoader.Load<PackedScene>("res://scenes/pause_menu/pause_menu_button.tscn").Instantiate<NPauseMenuButton>();
        exit.Name = "ExitSpectator";
        buttons.AddChild(exit);
        exit.GetNode<MegaLabel>("Label").SetTextAutoSize(_session.HasMeta("local_preview") ? L.T("menu.exit_preview") : L.T("menu.exit"));
        exit.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => RequestExit()));
        var visible = buttons.GetChildren().OfType<NButton>().Where(button => button.Visible).ToArray();
        for (int i = 0; i < visible.Length; i++)
        {
            visible[i].FocusNeighborTop = visible[(i + visible.Length - 1) % visible.Length].GetPath();
            visible[i].FocusNeighborBottom = visible[(i + 1) % visible.Length].GetPath();
        }
    }

    internal void SetPaused(bool paused)
    {
        if (_changingPause || _pause == null || _menuStack == null) return;
        _changingPause = true;
        try
        {
            StopDrawing();
            ClearHoverTips();
            if (!paused && _modalOverlay != null) NModalContainer.Instance?.Clear();
            _pause.ProcessMode = paused ? ProcessModeEnum.Always : ProcessModeEnum.Disabled;
            _pause.Visible = paused;
            if (_native != null)
            {
                Set(_native, "_isInputDisabled", paused);
                _native.MouseFilter = paused ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
                _native.ProcessMode = paused ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
            }
            if (_topBar != null) Set(_topBar.GetNode<NTopBarPauseButton>("%PauseButton"), "IsScreenOpen", paused);
            if (paused && _menuStack.Peek() == null)
            {
                Set(_pauseMenu!, "_runState", _displayRun);
                _menuStack.Push(_pauseMenu!);
                _pauseMenu!.DefaultFocusedControl?.TryGrabFocus();
            }
            if (!paused) while (_menuStack.Peek() != null) _menuStack.Pop();
            ActiveScreenContext.Instance.Update();
        }
        finally { _changingPause = false; }
    }

    internal void TogglePause() => SetPaused(_pause?.Visible != true);

    // 退出延迟到当前按钮事件结束，释放菜单焦点和热键后通知会话或本地预览。
    // Exit after the current button event, releasing menu focus and hotkeys before notifying its owner.
    private void RequestExit() => Callable.From(() =>
    {
        SetPaused(false);
        Hide();
        ExitRequested?.Invoke();
    }).CallDeferred();

    internal void ShowHoverTip(NHoverTipSet tip) => tip.Reparent(_hoverTips, true);

    // 设置页面的原版确认弹窗也显示在观战界面之上，关闭仍交给原版处理。
    // Keep native settings confirmations above the spectator UI and retain native dismissal.
    internal void ShowModal(Node modal)
    {
        if (_pause?.Visible != true) return;
        _modalOverlay = new Control { Name = "SpectatorModal" };
        _hoverTips.AddChild(_modalOverlay);
        _modalOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var backstop = new ColorRect { Color = new Color(0, 0, 0, 0.85f) };
        _modalOverlay.AddChild(backstop);
        backstop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        modal.Reparent(_modalOverlay, true);
    }
    internal void DismissModal()
    {
        _modalOverlay?.QueueFree();
        _modalOverlay = null;
    }
    private void ClearHoverTips()
    {
        if (!GodotObject.IsInstanceValid(_hoverTips)) return;
        foreach (var tip in _hoverTips.GetChildren().OfType<NHoverTipSet>().ToArray())
            if (AccessTools.Field(typeof(NHoverTipSet), "_owner").GetValue(tip) is Control owner) NHoverTipSet.Remove(owner);
    }
}
