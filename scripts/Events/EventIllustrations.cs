using Godot;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Random;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// Resolve illustrations through locally registered models. A received page never supplies executable scenes.
internal static class EventIllustrations
{
    internal static EventModel? Model(string id)
    {
        try { return ModelDb.GetByIdOrNull<EventModel>(ModelId.Deserialize(id)); }
        catch (Exception) { return null; }
    }

    internal static Texture2D? Portrait(EventModel? model, string path, bool phobia)
    {
        try
        {
            if (phobia && model?.HasPhobiaModePortrait == true) return model.CreateInitialPhobiaModePortrait();
            if (SpectatorEventView.IsTexturePath(path) && ResourceLoader.Exists(path))
                return ResourceLoader.Load<Texture2D>(path);
            // Native loading also respects asset substitutions and mods patching the native portrait factory.
            if (model?.LayoutType == EventLayoutType.Default) return model.CreateInitialPortrait();
        }
        catch (Exception) { }
        return null;
    }

    internal static Control? Background(EventModel? model, string? actId = null, int floor = 0)
    {
        if (model == null) return null;
        Control? scene = null;
        try
        {
            if (model.LayoutType == EventLayoutType.Ancient)
            {
                // Ancients use a scenery scene, not the default event's Portrait TextureRect.
                var container = new NAncientBgContainer();
                scene = container;
                container.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                container.AddChild(model.CreateBackgroundScene().Instantiate<Control>());
            }
            else if (model.LayoutType == EventLayoutType.Combat && model.CanonicalEncounter is { } encounter
                && !string.IsNullOrEmpty(actId) && ModelDb.GetByIdOrNull<ActModel>(ModelId.Deserialize(actId)) is { } act)
            {
                // Use the same native backdrop factory with an isolated visual RNG and mutable encounter clone.
                // No CombatState, monsters, event callbacks or run RNG are created or advanced.
                var container = new NCombatSceneContainer
                {
                    PivotOffset = new Vector2(960, 540), Scale = Vector2.One * encounter.GetCameraScaling()
                };
                scene = container;
                container.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                container.Position += encounter.GetCameraOffset();
                // Native backgrounds are drawn around the centered BgContainer, not the viewport's origin.
                // Reuse the original aspect-ratio helper with the same centered anchor and small horizontal offset.
                var center = new Control { Name = "BgContainer", UniqueNameInOwner = true };
                center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
                center.OffsetLeft = center.OffsetRight = 23;
                container.AddChild(center); center.Owner = container;
                center.AddChild(encounter.ToMutable().CreateBackground(act, new Rng((ulong)Math.Max(0, floor), model.Id.ToString())));
                container.Ready += () => container.Call(NCombatSceneContainer.MethodName.OnWindowChange);
            }
            else if (model is MegaCrit.Sts2.Core.Models.Events.FakeMerchant)
                scene = FakeMerchantEvent.CreateBackground();
            if (scene == null) return null;
            DisableInput(scene);
            return scene;
        }
        catch (Exception error)
        {
            scene?.Free();
            GD.PushWarning("[Spectator] Event illustration " + model.Id + ": " + error.Message);
            return null;
        }
    }

    private static void DisableInput(Node node)
    {
        node.SetProcessInput(false); node.SetProcessUnhandledInput(false); node.SetProcessUnhandledKeyInput(false);
        if (node is Control control) { control.MouseFilter = Control.MouseFilterEnum.Ignore; control.FocusMode = Control.FocusModeEnum.None; }
        foreach (var child in node.GetChildren()) DisableInput(child);
    }
}
