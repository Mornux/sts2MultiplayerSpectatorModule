using Godot;
using MegaCrit.Sts2.addons.mega_text;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 回车输入、回车发送、Esc 取消；独立叠层在地图和战斗中都能显示。
// Enter opens and submits, Escape cancels; an independent overlay works on maps and in combat.
public partial class SpectatorDanmaku : CanvasLayer
{
    public static bool IsEditing { get; private set; }
    private SpectatorSession _session = null!;
    private Control _root = null!, _editor = null!;
    private LineEdit _input = null!;
    private Label _hint = null!;
    private Font _font = null!;
    private readonly Queue<SpectatorSession.Danmaku> _pending = new();
    private readonly double[] _lanes = new double[6];
    private double _time;
    private const float Speed = 220;

    public void Initialize(SpectatorSession session)
    {
        _session = session;
        Layer = 180;
        ProcessMode = ProcessModeEnum.Always;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var template = ResourceLoader.Load<PackedScene>("res://scenes/ui/multiplayer_player_state.tscn").Instantiate<Control>();
        _font = template.GetNode<MegaLabel>("%NameplateLabel").GetThemeFont("font");
        template.Free();
        _editor = new Control { Name = "DanmakuInput", Visible = false };
        _root.AddChild(_editor);
        _editor.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color(0, 0, 0, 0.12f) };
        _editor.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var form = new VBoxContainer();
        _editor.AddChild(form);
        form.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        form.OffsetLeft = -460; form.OffsetRight = 460; form.OffsetTop = -130; form.OffsetBottom = -30;
        _input = new LineEdit { MaxLength = 100, PlaceholderText = L.T("chat.placeholder"), CustomMinimumSize = new Vector2(920, 56) };
        _input.AddThemeFontOverride("font", _font);
        _input.AddThemeFontSizeOverride("font_size", 30);
        form.AddChild(_input);
        _hint = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _hint.AddThemeFontOverride("font", _font);
        _hint.AddThemeFontSizeOverride("font_size", 24);
        form.AddChild(_hint);
        _session.DanmakuReceived += Receive;
        InitializeLog();
    }

    private void Receive(SpectatorSession.Danmaku message)
    {
        if (_pending.Count >= 32) _pending.Dequeue();
        _pending.Enqueue(message);
    }

    // 按文字宽度预留六条轨道，队列和屏幕数量都有上限。
    // Reserve six lanes by text width and bound both queued and visible messages.
    public override void _Process(double delta)
    {
        if (_session == null) return;
        _time += delta;
        UpdateLog(delta);
        if (!_session.CanSendDanmaku)
        {
            CloseEditor();
            _pending.Clear();
            foreach (var label in _root.GetChildren().OfType<Label>()) label.QueueFree();
            Array.Clear(_lanes);
            return;
        }
        for (int lane = 0; lane < _lanes.Length && _pending.Count > 0 && _root.GetChildCount() < 25; lane++)
        {
            if (_time < _lanes[lane]) continue;
            var message = _pending.Dequeue();
            var label = new Label { Text = message.Name + "：" + message.Text,
                MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(message.Color) };
            label.AddThemeFontOverride("font", _font);
            label.AddThemeFontSizeOverride("font_size", 32);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 6);
            _root.AddChild(label);
            float width = label.GetMinimumSize().X;
            label.Position = new Vector2(_root.Size.X + 20, 145 + lane * 44);
            _lanes[lane] = _time + (width + 70) / Speed;
            var tween = label.CreateTween();
            tween.TweenProperty(label, "position:x", -width - 20, (_root.Size.X + width + 40) / Speed);
            tween.TweenCallback(Callable.From(label.QueueFree));
        }
    }

    public override void _ExitTree()
    {
        if (_session != null) { _session.DanmakuReceived -= Receive; DisposeLog(); }
        if (_editor != null) CloseEditor();
    }
}
