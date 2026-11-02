using System.Text.Json;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 复制原版事件的纯展示控件，不创建 EventRoom，也不给选项连接点击回调。
// Copy native visual controls without creating an EventRoom or connecting option actions.
public partial class SpectatorEventView : Control, IScreenContext
{
    private TextureRect _portrait = null!;
    private MegaLabel _title = null!;
    private MegaRichTextLabel _description = null!, _history = null!;
    private VBoxContainer _options = null!;
    private Control _scenery = null!;
    private ScrollContainer _bodyScroll = null!;
    private Control _optionTemplate = null!;
    private EventState? _state;
    private ulong _selected;
    private string _shown = "";
    private string _illustration = "";
    public Control? DefaultFocusedControl => null;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color("10191e"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background); background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _scenery = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _scenery.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_scenery);
        var template = ResourceLoader.Load<PackedScene>("res://scenes/events/default_event_layout.tscn").Instantiate<Control>();
        _portrait = (TextureRect)template.GetNode<TextureRect>("%Portrait").Duplicate();
        _portrait.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_portrait);
        var body = (VBoxContainer)template.GetNode<VBoxContainer>("VBoxContainer").Duplicate();
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseForcePassScrollEvents = false };
        _bodyScroll = scroll;
        AddChild(scroll);
        scroll.AnchorLeft = scroll.AnchorRight = 0.5f; scroll.AnchorBottom = 1;
        scroll.OffsetLeft = -38; scroll.OffsetRight = 762; scroll.OffsetTop = 240; scroll.OffsetBottom = -30;
        scroll.AddChild(body);
        body.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        body.CustomMinimumSize = new Vector2(780, 0);
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title = body.GetNode<MegaLabel>("Title");
        _description = body.GetNode<MegaRichTextLabel>("EventDescription");
        _title.Modulate = _description.Modulate = Colors.White;
        _description.CustomMinimumSize = new Vector2(0, 180);
        _options = body.GetNode<VBoxContainer>("OptionsContainer");
        body.GetNode<Control>("SharedEventLabel").Hide();
        _history = (MegaRichTextLabel)_description.Duplicate();
        _history.CustomMinimumSize = Vector2.Zero;
        _history.Position = new Vector2(18, 470); _history.Size = new Vector2(520, 540);
        _history.FitContent = false; _history.ScrollActive = true;
        _history.MouseFilter = MouseFilterEnum.Stop; _history.MouseForcePassScrollEvents = false;
        _history.AddThemeFontSizeOverride("normal_font_size", 22);
        AddChild(_history);
        _optionTemplate = ResourceLoader.Load<PackedScene>("res://scenes/events/event_option_button.tscn").Instantiate<Control>();
        template.Free();
    }

    internal void Render(EventState state)
    {
        _state = state;
        if (!state.Players.Any(player => player.Id == _selected)) _selected = state.Players.FirstOrDefault()?.Id ?? 0;
        string shown = JsonSerializer.Serialize(state);
        if (_shown == shown) return;
        _shown = shown;
        var selected = state.Players.FirstOrDefault(player => player.Id == _selected);
        if (selected == null) return;
        _title.SetTextAutoSize(selected.Title + (selected.Finished ? L.T("events.finished") : ""));
        _description.Text = L.Resolve(selected.Description) + (selected.Details.Length > 0 ? "\n\n" + L.Resolve(selected.Details) : "");
        _description.VisibleRatio = 1;
        UpdateIllustration(selected);
        foreach (var child in _options.GetChildren()) { _options.RemoveChild(child); child.QueueFree(); }
        for (int index = 0; index < selected.Options.Length; index++)
        {
            var option = selected.Options[index];
            string votes = selected.Shared && option.Key.Length > 0 ? string.Join("、", state.Players
                .Where(player => player.Shared && player.EventId == selected.EventId && !player.Stale && player.VoteKey == option.Key)
                .Select(player => Escape(player.Name))) : "";
            AddOption(option, votes);
        }
        if (selected.Cells != null)
        {
            var grid = (MegaRichTextLabel)_description.Duplicate();
            grid.CustomMinimumSize = new Vector2(0, 270);
            grid.Text = string.Join('\n', selected.Cells.Select(row => string.Join(" ", row.Select(cell => cell switch { '#' => "■", '+' => "★", _ => "·" }))));
            grid.AddThemeFontSizeOverride("normal_font_size", 20);
            _options.AddChild(grid);
        }
        _history.Text = L.T("events.history") + string.Join("\n\n", state.Players.Select(player =>
            "[gold]" + Escape(player.Name) + "[/gold]\n" + (player.Choices.Length > 0
                ? string.Join('\n', player.Choices.TakeLast(6).Select(L.Resolve)) : L.T("events.no_choice"))));
    }

    internal void SwitchPlayer(int direction)
    {
        if (_state is not { Players.Length: > 1 } state) return;
        int index = Array.FindIndex(state.Players, player => player.Id == _selected);
        _selected = state.Players[(index + direction + state.Players.Length) % state.Players.Length].Id;
        _shown = "";
        Render(state);
    }

    internal bool IsScrollingContent(Vector2 viewportPosition)
    {
        bool Over(Control control) => new Rect2(Vector2.Zero, control.Size).HasPoint(
            control.GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition);
        var bar = _bodyScroll.GetVScrollBar();
        return bar.MaxValue > bar.Page && Over(_bodyScroll)
            || _history.GetContentHeight() > _history.Size.Y && Over(_history);
    }

    private void UpdateIllustration(EventPlayerState player)
    {
        bool phobia = MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.PhobiaMode;
        var map = SpectatorMap.Find(this)?.Snapshot;
        string key = _state?.Key + "\n" + player.EventId + "\n" + player.Portrait + "\n" + phobia + "\n" + map?.ActId;
        if (_illustration == key) return;
        _illustration = key;
        foreach (var child in _scenery.GetChildren()) { _scenery.RemoveChild(child); child.QueueFree(); }
        var model = EventIllustrations.Model(player.EventId);
        _portrait.Texture = EventIllustrations.Portrait(model, player.Portrait, phobia);
        if (EventIllustrations.Background(model, map?.ActId, map?.Floor ?? 0) is not { } scene) return;
        _scenery.AddChild(scene);
    }

    private void AddOption(EventOptionState option, string votes)
    {
        var row = new PanelContainer { CustomMinimumSize = new Vector2(0, 100), MouseFilter = MouseFilterEnum.Ignore };
        var image = (NinePatchRect)_optionTemplate.GetNode<NinePatchRect>("Image").Duplicate();
        image.MouseFilter = MouseFilterEnum.Ignore;
        if (option.Locked) image.Modulate = new Color(0.65f, 0.65f, 0.65f);
        row.AddChild(image);
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (string edge in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + edge, 24);
        foreach (string edge in new[] { "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 12);
        row.AddChild(margin);
        var content = new HBoxContainer(); margin.AddChild(content);
        if (Texture(option.Relic) is { } relic)
            content.AddChild(new TextureRect { Texture = relic, CustomMinimumSize = new Vector2(56, 56),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        var text = (MegaRichTextLabel)_optionTemplate.GetNode<MegaRichTextLabel>("%Text").Duplicate();
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.CustomMinimumSize = new Vector2(600, 0);
        text.FitContent = true; text.ScrollActive = false; text.MouseFilter = MouseFilterEnum.Ignore;
        text.Text = (option.Title.Length == 0 ? "" : "[b][" + (option.Locked ? "red" : "gold") + "]" + option.Title
            + "[/" + (option.Locked ? "red" : "gold") + "][/b]\n")
            + option.Description + (votes.Length > 0 ? L.T("events.voters", votes) : "");
        content.AddChild(text);
        _options.AddChild(row);
    }

    internal static bool IsTexturePath(string? path) => path is { Length: > 6 and <= 512 }
        && path.StartsWith("res://", StringComparison.Ordinal) && !path.Contains('\\')
        && !path.Any(char.IsControl) && !path.Split('/').Contains("..", StringComparer.Ordinal)
        && (path.StartsWith("res://images/", StringComparison.Ordinal)
            || new[] { ".png", ".jpg", ".jpeg", ".webp", ".svg" }.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)));

    private static Texture2D? Texture(string path)
    {
        if (!IsTexturePath(path)) return null;
        // Keep the original images/ lookup. Outside it, only add local image files;
        // do not extend remote page loading to scenes, scripts or arbitrary resources.
        try { return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null; }
        catch (Exception) { return null; }
    }
    private static string Escape(string text) => text.Replace("[", "[lb]");
    public override void _ExitTree() => _optionTemplate?.Free();
}
