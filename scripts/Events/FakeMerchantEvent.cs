using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Events;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 伪商人展示库存，不实例化可购买的商店控件。
// Show Fake Merchant inventory without instantiating purchase controls.
internal static class FakeMerchantEvent
{
    internal static Control CreateBackground()
    {
        // Extract only the original scenery before the custom event can enter the tree.
        // Its inventory, event script, merchant hitbox and proceed button never become active.
        var template = ResourceLoader.Load<PackedScene>("res://scenes/events/custom/fake_merchant.tscn").Instantiate<Control>();
        Control? scenery = null;
        try
        {
            scenery = template.GetNode<Control>("SceneContainer");
            template.RemoveChild(scenery);
            var button = scenery.GetNode<Control>("MerchantButton");
            var placement = new Control
            {
                Name = "MerchantIllustration", AnchorLeft = button.AnchorLeft, AnchorRight = button.AnchorRight,
                AnchorTop = button.AnchorTop, AnchorBottom = button.AnchorBottom,
                OffsetLeft = button.OffsetLeft, OffsetRight = button.OffsetRight,
                OffsetTop = button.OffsetTop, OffsetBottom = button.OffsetBottom,
                GrowHorizontal = button.GrowHorizontal, GrowVertical = button.GrowVertical,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            var visual = button.GetNode<Node2D>("MerchantVisual");
            button.RemoveChild(visual); placement.AddChild(visual);
            scenery.AddChild(placement); scenery.MoveChild(placement, button.GetIndex());
            button.Free();
            placement.Ready += () => placement.RunWhenSpineReady(new MegaSprite(visual), animation => animation.SetAnimation("idle_loop"));
            return scenery;
        }
        catch { scenery?.Free(); throw; }
        finally { template.Free(); }
    }

    internal static EventPlayerState Enrich(FakeMerchant model, EventPlayerState state) => state with
    {
        Details = L.Wire("events.merchant") + string.Join("\n", model.Inventory.RelicEntries.Select(entry =>
            entry.IsStocked ? entry.Model!.Title.GetFormattedText() + " · " + entry.Cost + L.Wire("events.gold") : L.Wire("events.sold")))
            + (model.StartedFight ? L.Wire("events.fight") : L.Wire("events.debug_merchant"))
    };
}
