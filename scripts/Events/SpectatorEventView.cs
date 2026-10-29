using System.Text.Json;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 复制原版事件的纯展示控件，不创建 EventRoom，也不给选项连接点击回调。
// Copy native visual controls without creating an EventRoom or connecting option actions.
public partial class SpectatorEventView : Control, IScreenContext
{
    private TextureRect _portrait = null!;
    private MegaLabel _title = null!, _hint = null!;
    private MegaRichTextLabel _description = null!, _history = null!;
    private VBoxContainer _options = null!;
    private HBoxContainer _tabs = null!;
    private Control _optionTemplate = null!;
    private EventState? _state;
    private ulong _selected;
    private string _shown = "";
    public Control? DefaultFocusedControl => _tabs?.GetChildren().OfType<NButton>().FirstOrDefault();

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color("10191e"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background); background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var template = ResourceLoader.Load<PackedScene>("res://scenes/events/default_event_layout.tscn").Instantiate<Control>();
        _portrait = (TextureRect)template.GetNode<TextureRect>("%Portrait").Duplicate();
        _portrait.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_portrait);
        var body = (VBoxContainer)template.GetNode<VBoxContainer>("VBoxContainer").Duplicate();
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
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
        _hint = (MegaLabel)_title.Duplicate();
        _hint.AddThemeFontSizeOverride("font_size", 24);
        _hint.Position = new Vector2(360, 130); _hint.Size = new Vector2(1200, 40);
        _hint.SetTextAutoSize(L.T("events.help"));
        AddChild(_hint);
        _tabs = new HBoxContainer { Position = new Vector2(380, 180), Size = new Vector2(1300, 52) };
        AddChild(_tabs);
        _history = (MegaRichTextLabel)_description.Duplicate();
        _history.CustomMinimumSize = Vector2.Zero;
        _history.Position = new Vector2(18, 470); _history.Size = new Vector2(520, 540);
        _history.FitContent = false; _history.ScrollActive = true;
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
        foreach (var child in _tabs.GetChildren()) { _tabs.RemoveChild(child); child.QueueFree(); }
        foreach (var player in state.Players)
        {
            var button = ResourceLoader.Load<PackedScene>("res://scenes/pause_menu/pause_menu_button.tscn").Instantiate<NPauseMenuButton>();
            button.CustomMinimumSize = new Vector2(160, 36);
            var holder = new Control { CustomMinimumSize = new Vector2(180, 38) };
            _tabs.AddChild(holder);
            holder.AddChild(button);
            button.Size = new Vector2(240, 48);
            button.Scale = new Vector2(0.75f, 0.75f);
            button.GetNode<MegaLabel>("Label").SetTextAutoSize(player.Name);
            button.Modulate = player.Id == _selected ? Colors.White : new Color(0.65f, 0.65f, 0.65f);
            button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
            { _selected = player.Id; _shown = ""; Render(_state!); }));
        }
        var selected = state.Players.FirstOrDefault(player => player.Id == _selected);
        if (selected == null) return;
        _title.SetTextAutoSize(selected.Title + (selected.Finished ? L.T("events.finished") : ""));
        _description.Text = L.Resolve(selected.Description) + (selected.Details.Length > 0 ? "\n\n" + L.Resolve(selected.Details) : "");
        _description.VisibleRatio = 1;
        var portrait = Texture(selected.Portrait);
        string id = selected.EventId.Split('.').Last().ToLowerInvariant();
        if (MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.PhobiaMode
            && Texture("res://images/events/" + id + "_phobia_mode.png") is { } phobia) portrait = phobia;
        if (portrait == null)
        {
            portrait = Texture("res://images/events/" + id + ".png");
        }
        _portrait.Texture = portrait;
        foreach (var child in _options.GetChildren()) { _options.RemoveChild(child); child.QueueFree(); }
        for (int index = 0; index < selected.Options.Length; index++)
        {
            var option = selected.Options[index];
            string votes = selected.Shared ? string.Join("、", state.Players.Where(player => player.Vote == index).Select(player => Escape(player.Name))) : "";
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
            "[gold]" + Escape(player.Name) + "[/gold]\n" + (player.Choices.Length > 0 ? string.Join('\n', player.Choices.TakeLast(6).Select(L.Resolve)) : L.T("events.no_choice"))
            + (!player.LocalPage ? L.T("events.host_state") : "")));
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
        text.Text = "[b][" + (option.Locked ? "red" : "gold") + "]" + option.Title + "[/" + (option.Locked ? "red" : "gold")
            + "][/b]\n" + option.Description + (votes.Length > 0 ? L.T("events.voters", votes) : "");
        content.AddChild(text);
        _options.AddChild(row);
    }

    private static Texture2D? Texture(string path) => path.StartsWith("res://images/", StringComparison.Ordinal)
        && ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
    private static string Escape(string text) => text.Replace("[", "[lb]");
    public override void _ExitTree() => _optionTemplate?.Free();
}
