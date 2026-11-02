using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// Native scene, background, character animation and button visuals with no gameplay option attached.
public partial class SpectatorRestSiteView : Control, IScreenContext
{
    private NRestSiteRoom? _room;
    private MegaRichTextLabel _history = null!;
    private string _roomKey = "", _shown = "";
    public Control? DefaultFocusedControl => null;

    internal static SpectatorRestSiteView? Find(Node node)
    {
        for (Node? current = node; current != null; current = current.GetParent())
            if (current is SpectatorRestSiteView view) return view;
        return null;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var template = ResourceLoader.Load<PackedScene>("res://scenes/rooms/rest_site_room.tscn").Instantiate<Control>();
        _history = (MegaRichTextLabel)template.GetNode<MegaRichTextLabel>("%Description").Duplicate();
        _history.Name = "RestSiteHistory"; _history.UniqueNameInOwner = false;
        _history.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _history.Position = new Vector2(24, 470); _history.Size = new Vector2(560, 400);
        _history.BbcodeEnabled = true; _history.FitContent = false; _history.ScrollActive = true;
        _history.Modulate = Colors.White; _history.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_history);
        _history.AddThemeFontSizeOverride("normal_font_size", 24);
        var hint = (MegaLabel)template.GetNode<MegaLabel>("%Header").Duplicate();
        hint.Name = "RestSiteHint"; hint.UniqueNameInOwner = false;
        hint.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        hint.Position = new Vector2(650, 95); hint.Size = new Vector2(1100, 40);
        template.Free();
        AddChild(hint);
        hint.SetTextAutoSize(L.T("rest.help"));
    }

    internal void BindRoom(NRestSiteRoom room)
    {
        var header = room.GetNode<MegaLabel>("%Header");
        var description = room.GetNode<MegaRichTextLabel>("%Description");
        SpectatorMap.Set(room, "Header", header);
        SpectatorMap.Set(room, "Description", description);
        SpectatorMap.Set(room, "_choicesContainer", room.GetNode<Control>("%ChoicesContainer"));
        SpectatorMap.Set(room, "_choicesScreen", room.GetNode<Control>("%ChoicesScreen"));
        SpectatorMap.Set(room, "_proceedButton", room.GetNode<NProceedButton>("%ProceedButton"));
        header.SetTextAutoSize(new LocString("rest_site_ui", "PROMPT").GetFormattedText());
        description.Modulate = Colors.White;
        room.GetNode<NProceedButton>("%ProceedButton").Disable();
        room.GetNode<Control>("%ProceedButton").Hide();
    }

    internal void Render(RestSiteState state)
    {
        string shown = System.Text.Json.JsonSerializer.Serialize(state);
        if (_shown == shown) return;
        string key = state.Key + ":" + state.ActId + ":" + string.Join(',', state.Players.Select(player => player.Id + ":" + player.Character));
        if (_roomKey != key)
        {
            _room?.Free();
            _room = null;
            BuildRoom(state);
            _roomKey = key;
        }
        var host = state.Players.First(player => player.Id == state.HostId);
        var choices = _room!.GetNode<Control>("%ChoicesContainer");
        foreach (var child in choices.GetChildren()) { choices.RemoveChild(child); child.QueueFree(); }
        foreach (var option in host.Options)
        {
            var button = ResourceLoader.Load<PackedScene>("res://scenes/rest_site/rest_site_button.tscn").Instantiate<NRestSiteButton>();
            choices.AddChild(button);
            button.GetNode<MegaLabel>("%Label").SetTextAutoSize(option.Title);
            var icon = button.GetNode<TextureRect>("%Icon");
            if (Events.SpectatorEventView.IsTexturePath(option.Icon) && ResourceLoader.Exists(option.Icon))
                icon.Texture = ResourceLoader.Load<Texture2D>(option.Icon);
            SpectatorMap.Set(button, "_isUnclickable", !option.Enabled);
            if (!option.Enabled && icon.Material is ShaderMaterial hsv)
            { hsv.SetShaderParameter("s", 0f); hsv.SetShaderParameter("v", 0.6f); }
            button.TooltipText = option.Description;
            button.MouseEntered += () => _room.SetText(option.Description);
            button.FocusEntered += () => _room.SetText(option.Description);
            button.MouseExited += () => ShowPlayerSummary(host);
        }
        _history.Text = L.T("events.history") + string.Join("\n\n", state.Players.Select(player =>
            "[gold]" + Escape(player.Name) + "[/gold]\n" + (player.Choices.Length == 0 ? L.T("events.no_choice")
                : string.Join('\n', player.Choices.Select(Escape)))));
        ShowPlayerSummary(host);
        _shown = shown;
    }

    private void ShowPlayerSummary(RestSitePlayerState player) => _room?.SetText(player.Name
        + (player.Options.Length == 0 ? L.T("events.finished") : "")
        + (player.Choices.Length == 0 ? "" : "\n" + string.Join(" · ", player.Choices)));

    private void BuildRoom(RestSiteState state)
    {
        var room = ResourceLoader.Load<PackedScene>("res://scenes/rooms/rest_site_room.tscn").Instantiate<NRestSiteRoom>();
        AddChild(room);
        MoveChild(room, 0);
        _room = room;
        _history.AddThemeFontOverride("normal_font", room.GetNode<MegaRichTextLabel>("%Description").GetThemeFont("normal_font"));
        var act = ModelDb.GetById<ActModel>(ModelId.Deserialize(state.ActId));
        var background = ResourceLoader.Load<PackedScene>(act.RestSiteBackgroundPath).Instantiate<Control>();
        var bg = room.GetNode<Control>("BgContainer");
        bg.AddChild(background); bg.MoveChild(background, 0);
        var models = state.Players.Select(player => SpectatorDisplayModels.Create(
            ModelDb.GetById<CharacterModel>(ModelId.Deserialize(player.Character)), player.Id)).ToArray();
        var display = RunState.CreateForTest(models, seed: "SPECTATOR_REST_SITE");
        SpectatorMap.Set(display, "Acts", Enumerable.Repeat(act, Math.Max(1, state.Act + 1)).ToArray());
        display.CurrentActIndex = state.Act;
        for (int index = 0; index < models.Length; index++)
        {
            // The current native animation node throws outside acts 0..2; keep future acts viewable.
            if (state.Act > 2)
            {
                bg.GetNode<Control>("Character_" + (index + 1)).AddChild(new TextureRect
                {
                    Texture = models[index].Character.IconTexture, CustomMinimumSize = new Vector2(160, 160),
                    MouseFilter = MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
                });
                continue;
            }
            var character = NRestSiteCharacter.Create(models[index], index);
            bg.GetNode<Control>("Character_" + (index + 1)).AddChild(character);
            character.Position = Vector2.Zero;
            if (index % 2 == 1) character.FlipX();
            character.Hitbox.MouseFilter = MouseFilterEnum.Ignore;
            character.Hitbox.FocusMode = FocusModeEnum.None;
            room.characterAnims.Add(character); room.Characters.Add(character);
        }
        // This room is a child of the spectator map; it is never assigned to NRun or RunManager.
    }

    internal void Clear()
    {
        if (_room != null && GodotObject.IsInstanceValid(_room)) _room.Free();
        _room = null; _roomKey = _shown = "";
        _history.Text = "";
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
