using Godot;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 左下角保留最近一百条消息，平时浮起淡出，输入弹幕时显示可滚动记录。
// Keep one hundred recent messages; float and fade notices, or show scrollable history while typing.
public partial class SpectatorDanmaku
{
    private readonly Queue<string> _history = new();
    private readonly Queue<(RichTextLabel Label, double Time)> _notices = new();
    private Control _log = null!;
    private VBoxContainer _transient = null!;
    private RichTextLabel _archive = null!;
    private bool _logWasEditing;
    private string _logContext = "";
    private double _noticeTime;

    private void InitializeLog()
    {
        ActivityCapture.Session = _session;
        _log = new Control { Name = "SpectatorMessageLog", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_log);
        _log.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _log.OffsetLeft = 24; _log.OffsetRight = 624;
        _log.OffsetTop = -430; _log.OffsetBottom = -150;
        _transient = new VBoxContainer { CustomMinimumSize = new Vector2(600, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _archive = LogLabel();
        _archive.ScrollActive = true; _archive.ScrollFollowing = true;
        _archive.MouseFilter = Control.MouseFilterEnum.Stop;
        _archive.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0.025f, 0.04f, 0.055f, 0.85f),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10 });
        _log.AddChild(_transient); _log.AddChild(_archive);
        _archive.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _archive.Hide();
        _logContext = _session.MessageLogContext;
        _session.ActivityReceived += ReceiveActivity;
        _session.DanmakuReceived += RecordChat;
    }

    private RichTextLabel LogLabel(bool fit = false)
    {
        var label = new RichTextLabel { BbcodeEnabled = true, ScrollActive = false, FitContent = fit,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("normal_font", _font);
        label.AddThemeFontSizeOverride("normal_font_size", 22);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }

    private static string EscapeLog(string text) => text.Replace("[", "[lb]");
    private void ReceiveActivity(ActivityNotice notice) => AppendLog(L.T(
        notice.Action is "choice" or "选择了" ? "activity.choice" : "activity.obtain", EscapeLog(notice.Name),
        "[color=#" + notice.Color + "]" + EscapeLog(notice.Text) + "[/color]"));
    private void RecordChat(SpectatorSession.Danmaku message) => AppendLog(
        "[color=#" + message.Color + "]" + EscapeLog(message.Name) + "[/color]：" + EscapeLog(message.Text));

    private void AppendLog(string text)
    {
        UpdateLog(0);
        if (_history.Count >= 100) _history.Dequeue();
        _history.Enqueue(text);
        if (IsEditing) _archive.Text = string.Join('\n', _history);
        var label = LogLabel(true);
        label.Text = text;
        _transient.AddChild(label);
        _notices.Enqueue((label, _noticeTime));
        if (_notices.Count > 6) RemoveNotice();
        RefreshLog();
    }

    private void RefreshLog()
    {
        while (_notices.TryPeek(out var first) && _noticeTime - first.Time >= 2.2) RemoveNotice();
        float newestAge = 0;
        foreach (var notice in _notices)
        {
            newestAge = (float)(_noticeTime - notice.Time);
            notice.Label.Modulate = new Color(1, 1, 1, Math.Clamp((2.2f - newestAge) / 0.6f, 0, 1));
        }
        _transient.Position = new Vector2(0, Math.Max(0, _log.Size.Y - _transient.GetCombinedMinimumSize().Y) - Math.Min(newestAge * 14, 28));
        _archive.Visible = IsEditing;
        _transient.Visible = !IsEditing;
        _log.Visible = _session.CanSendDanmaku;
        if (IsEditing && !_logWasEditing)
        {
            _archive.Text = string.Join('\n', _history);
            _archive.ScrollToLine(Math.Max(0, _archive.GetLineCount() - 1));
        }
        _logWasEditing = IsEditing;
    }

    // 只更新最多六个提示的透明度，不重排历史文字或解析颜色标签。
    // Update opacity on at most six notices without rebuilding history or parsing color tags.
    private void RemoveNotice()
    {
        var label = _notices.Dequeue().Label;
        _transient.RemoveChild(label);
        label.QueueFree();
    }

    private void UpdateLog(double delta)
    {
        _noticeTime += delta;
        string context = _session.MessageLogContext;
        if (_logContext != context)
        {
            _logContext = context; _history.Clear(); _archive.Text = "";
            while (_notices.Count > 0) RemoveNotice();
        }
        RefreshLog();
    }

    private void DisposeLog()
    {
        _session.ActivityReceived -= ReceiveActivity;
        _session.DanmakuReceived -= RecordChat;
        if (ReferenceEquals(ActivityCapture.Session, _session)) ActivityCapture.Session = null;
    }
}
