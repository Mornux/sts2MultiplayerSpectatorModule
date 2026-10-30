using System.Globalization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 只报告金币的实际增加，消费及读档不会产生获取提示。
// Report positive gold changes; spending and deserialized players are ignored.
[HarmonyPatch(typeof(Player), nameof(Player.Gold), MethodType.Setter)]
internal static class SpectatorActivityGold
{
    private static void Prefix(Player __instance, out int __state) => __state = __instance.Gold;
    private static void Postfix(Player __instance, int __state)
    {
        int gained = __instance.Gold - __state;
        if (gained > 0) ActivityCapture.Record(__instance, "gold", () => gained.ToString(CultureInfo.InvariantCulture));
    }
}

// 只追踪实际牌堆中的卡牌，跳过升级预览及读档副本。
// Track cards in actual piles, excluding upgrade previews and deserialized copies.
[HarmonyPatch(typeof(CardModel), nameof(CardModel.UpgradeInternal))]
internal static class SpectatorActivityUpgrade
{
    private static void Prefix(CardModel __instance, out int __state) => __state = __instance.CurrentUpgradeLevel;
    private static void Postfix(CardModel __instance, int __state)
    {
        if (__instance.CurrentUpgradeLevel > __state && __instance.Pile != null)
            ActivityCapture.Record(__instance.Owner, "upgrade", () => __instance.Title, ActivityCapture.CardColor(__instance.Rarity));
    }
}

// 在成功附魔后报告，也包含同类附魔叠加的情况。
// Report successful enchantments, including stacking an existing enchantment.
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Enchant), [typeof(EnchantmentModel), typeof(CardModel), typeof(decimal)])]
internal static class SpectatorActivityEnchant
{
    private static void Postfix(CardModel card, EnchantmentModel? __result)
    {
        if (__result != null && card.Pile != null)
            ActivityCapture.Record(card.Owner, "enchant", () => card.Title + " · " + __result.Title.GetFormattedText(),
                ActivityCapture.CardColor(card.Rarity));
    }
}
