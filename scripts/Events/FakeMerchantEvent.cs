using MegaCrit.Sts2.Core.Models.Events;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 伪商人展示库存，不实例化可购买的商店控件。
// Show Fake Merchant inventory without instantiating purchase controls.
internal static class FakeMerchantEvent
{
    internal static EventPlayerState Enrich(FakeMerchant model, EventPlayerState state) => state with
    {
        Details = L.Wire("events.merchant") + string.Join("\n", model.Inventory.RelicEntries.Select(entry =>
            entry.IsStocked ? entry.Model!.Title.GetFormattedText() + " · " + entry.Cost + L.Wire("events.gold") : L.Wire("events.sold")))
            + (model.StartedFight ? L.Wire("events.fight") : L.Wire("events.debug_merchant"))
    };
}
