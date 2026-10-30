using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 地图名单和观众头像共用原版外观，只按需要复制生命条。
// Share native roster visuals between map participants and viewers, optionally including health.
internal static class RosterVisuals
{
    internal static (Control Root, MegaLabel Name, NHealthBar? Health) Create(Texture2D texture, bool withHealth = false)
    {
        var template = ResourceLoader.Load<PackedScene>("res://scenes/ui/multiplayer_player_state.tscn").Instantiate<Control>();
        var root = new Control { Size = new Vector2(280, 64), CustomMinimumSize = new Vector2(280, 64),
            MouseFilter = Control.MouseFilterEnum.Ignore };
        var icon = (TextureRect)template.GetNode<TextureRect>("%CharacterIcon").Duplicate();
        foreach (var child in icon.GetChildren()) { icon.RemoveChild(child); child.Free(); }
        icon.UniqueNameInOwner = false;
        icon.Texture = texture;
        var name = (MegaLabel)template.GetNode<MegaLabel>("%NameplateLabel").Duplicate();
        name.UniqueNameInOwner = false;
        name.Position = new Vector2(67, 6);
        name.Size = new Vector2(210, 32);
        root.AddChild(icon);
        root.AddChild(name);
        NHealthBar? health = null;
        if (withHealth)
        {
            health = (NHealthBar)template.GetNode<NHealthBar>("%HealthBar").Duplicate();
            health.UniqueNameInOwner = false;
            root.AddChild(health);
        }
        template.Free();
        foreach (var child in root.FindChildren("*", owned: false).OfType<Control>())
            child.MouseFilter = Control.MouseFilterEnum.Ignore;
        return (root, name, health);
    }
}
