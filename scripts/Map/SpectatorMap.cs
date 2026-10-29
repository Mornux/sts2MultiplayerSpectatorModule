using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Runs;
using static sts2MultiplayerSpectatorModule.Scripts.SpectatorSession;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 使用原版地图与顶部栏场景，显示状态独立于真实跑局。
// Use native map and top bar scenes with display state isolated from the real run.
public partial class SpectatorMap : Control
{
    private SpectatorSession _session = null!;
    private NMapScreen? _native;
    private NTopBar? _topBar;
    private RunState? _displayRun;
    private PeerInputSynchronizer? _input;
    private Control? _eventContent;
    internal bool IsPaused => _pause?.Visible == true;
    internal static SpectatorMap? Active { get; private set; }
    internal MapSnapshot? Snapshot => _session.Map;
    internal IScreenContext? CurrentScreen => _inspection as IScreenContext ?? (_pause?.Visible == true ? _menuStack?.Peek() : _eventContent as IScreenContext ?? _native);

    // 事件覆盖地图中央，顶部栏、名单和齿轮仍由同一个观战界面提供。
    // Overlay events while retaining the spectator top bar, roster, and gear menu.
    internal void SetEventContent(Control? content)
    {
        if (_native != null) _native.Visible = content == null;
        if (_eventContent == content) return;
        _eventContent?.Hide();
        _eventContent = content;
        if (content != null)
        {
            content.Show();
            MoveChild(content, _topBar?.GetIndex() ?? 0);
        }
        MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.Update();
    }
    private Label _status = null!;
    private string? _key, _failedKey;
    private MapCoord? _current;
    private bool _blocksHotkeys;
    public event Action? ExitRequested;
    public event Action? ReturnToCombatRequested;
    private bool _combatAvailable;

    // 战斗中的地图只是覆盖层，返回战斗不退出观战会话。
    // Treat the combat map as an overlay; returning does not end the spectator session.
    public void SetCombatAvailable(bool available)
    {
        _combatAvailable = available;
        if (_native?.GetNode<NBackButton>("Back") is not { } back) return;
        back.Visible = available;
        if (available && IsVisibleInTree()) back.Enable(); else back.Disable();
    }

    internal static SpectatorMap? Find(Node node)
    {
        for (Node? parent = node; parent != null; parent = parent.GetParent())
            if (parent is SpectatorMap map) return map;
        return null;
    }

    internal static void Set(object target, string name, object? value)
    {
        var field = AccessTools.Field(target.GetType(), name);
        if (field != null) field.SetValue(target, value);
        else
        {
            var property = AccessTools.Property(target.GetType(), name);
            AccessTools.DeclaredProperty(property.DeclaringType, name).SetValue(target, value);
        }
    }

    internal static object? Call(object target, string name, params object[] args) =>
        AccessTools.Method(target.GetType(), name).Invoke(target, args);

    public void Initialize(SpectatorSession session)
    {
        _session = session;
        session.BattleReceived += ReceiveInventory;
        _session.SetDrawingColor(LoadDrawingColor());
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color("10191e"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _status = new Label { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(110, 110) };
        AddChild(_status);
        _hoverTips = new Control { Name = "SpectatorHoverTips", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_hoverTips);
        _hoverTips.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        VisibilityChanged += () =>
        {
            if (IsVisibleInTree())
            {
                Active = this;
                if (!_blocksHotkeys && NHotkeyManager.Instance is { } hotkeys)
                { hotkeys.AddBlockingScreen(this); _blocksHotkeys = true; }
                _topBar?.GetNode<NTopBarPauseButton>("%PauseButton").Enable();
                if (_combatAvailable) _native?.GetNode<NBackButton>("Back").Enable();
                Refresh();
            }
            else
            {
                if (Active == this) Active = null;
                SetPaused(false);
                ClearHoverTips();
                _topBar?.GetNode<NTopBarPauseButton>("%PauseButton").Disable();
                _native?.GetNode<NBackButton>("Back").Disable();
                UnblockHotkeys();
            }
            ActiveScreenContext.Instance.Update();
        };
        Refresh();
    }

    public void Refresh()
    {
        _status.Text = _failedKey == _session.Map?.Key && _failedKey != null
            ? L.T("map.load_failed") : _session.Status;
        if (!IsVisibleInTree()) return;
        var snapshot = _session.Map;
        if (snapshot == null) { DestroyMap(); return; }
        if (_failedKey == snapshot.Key) return;
        if (_native == null || _key != snapshot.Key)
        {
            try { CreateMap(snapshot); }
            catch (Exception error)
            {
                _failedKey = snapshot.Key;
                DestroyMap();
                _status.Text = L.T("map.load_failed");
                GD.PushError($"[Spectator] Native map: {error}");
                return;
            }
        }
        _timerLabel?.SetTextAutoSize(TimeFormatting.Format(snapshot.RunTime));
        UpdateRoomInfo(snapshot);
        UpdatePlayerList(snapshot);
        UpdateProgress(snapshot);
        RefreshInk();
    }

    public override void _Input(InputEvent input)
    {
        if (SpectatorDanmaku.IsEditing) return;
        if (!IsVisibleInTree()) return;
        if (_inspection != null)
        {
            if (input.IsActionPressed(MegaCrit.Sts2.Core.ControllerInput.MegaInput.pauseAndBack) && !input.IsEcho())
            { CloseInventory(); GetViewport().SetInputAsHandled(); }
            return;
        }
        if (NModalContainer.Instance?.OpenModal != null) return;
        if (_pause?.Visible == true)
        {
            if (input.IsActionPressed(MegaCrit.Sts2.Core.ControllerInput.MegaInput.pauseAndBack) && !input.IsEcho())
            {
                if (_menuStack?.Peek() == _pauseMenu) SetPaused(false);
                else _menuStack?.Pop();
                GetViewport().SetInputAsHandled();
            }
            else if (input.IsActionReleased(MegaCrit.Sts2.Core.ControllerInput.MegaInput.pauseAndBack)) GetViewport().SetInputAsHandled();
            return;
        }
        if (_combatAvailable && input.IsActionPressed(MegaCrit.Sts2.Core.ControllerInput.MegaInput.viewMap) && !input.IsEcho())
        { ReturnToCombatRequested?.Invoke(); GetViewport().SetInputAsHandled(); return; }
        if (input is not InputEventKey { Keycode: Key.Escape }) return;
        if (input is InputEventKey { Pressed: true, Echo: false })
        {
            if (_combatAvailable && _pause?.Visible != true) ReturnToCombatRequested?.Invoke();
            else if (_native == null) RequestExit();
            else TogglePause();
        }
        GetViewport().SetInputAsHandled();
    }

    private void DestroyMap()
    {
        SetPaused(false);
        if (_native != null) { _native.Free(); _native = null; }
        _input?.Dispose();
        _input = null;
        _key = null;
        _pause?.Hide();
    }

    private void UnblockHotkeys()
    {
        if (!_blocksHotkeys) return;
        NHotkeyManager.Instance?.RemoveBlockingScreen(this);
        _blocksHotkeys = false;
    }

    public override void _ExitTree()
    {
        _session.BattleReceived -= ReceiveInventory;
        SetPaused(false);
        if (Active == this) Active = null;
        ClearHoverTips();
        UnblockHotkeys();
        _input?.Dispose();
    }
}
