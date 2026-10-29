using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 观察真实跑局的成功获取，不执行奖励，不记录静默读档或观战回放。
// Observe successful acquisitions without granting rewards or recording loads and replays.
internal static class ActivityCapture
{
    internal static SpectatorSession? Session;
    internal static void Record(Player? player, string action, Func<string> title, string color = "edc87d")
    {
        try
        {
            if (player == null || Session is not { IsHost: true, HasViewer: true, ReplayRunning: false }) return;
            var run = RunManager.Instance.DebugOnlyGetState();
            if (run?.BaseRoom == null || !run.Players.Contains(player)) return;
            string text = System.Text.RegularExpressions.Regex.Replace(title(), @"\[[^\]]*\]", "");
            text = new string(text.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
            if (text.Length > 500) text = text[..500];
            Session.PublishActivity(player.NetId, action, text, color);
        }
        catch (Exception error) { GD.PushWarning("[Spectator] Activity: " + error.Message); }
    }

    // 稀有、罕见、普通及特殊卡牌使用不同的名称颜色。
    // Distinguish rare, uncommon, common and special card names by color.
    internal static string CardColor(CardRarity rarity) => rarity switch
    {
        CardRarity.Rare => "edc87d",
        CardRarity.Uncommon => "87ceeb",
        CardRarity.Curse => "c597ef",
        CardRarity.Event or CardRarity.Ancient or CardRarity.Quest => "8bdb9b",
        CardRarity.Status => "b3b3b3",
        _ => "fff4df"
    };
}

[HarmonyPatch(typeof(Player), nameof(Player.AddRelicInternal))]
internal static class SpectatorActivityRelic
{
    private static void Postfix(Player __instance, RelicModel relic, bool silent)
    {
        if (!silent) ActivityCapture.Record(__instance, "obtain", () => relic.Title.GetFormattedText());
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.AddPotionInternal))]
internal static class SpectatorActivityPotion
{
    private static void Postfix(Player __instance, PotionModel potion, bool silent, PotionProcureResult __result)
    {
        if (!silent && __result.success) ActivityCapture.Record(__instance, "obtain", () => potion.Title.GetFormattedText());
    }
}

[HarmonyPatch(typeof(CardPile), nameof(CardPile.AddInternal))]
internal static class SpectatorActivityCard
{
    private static void Postfix(CardPile __instance, CardModel card, bool silent)
    {
        if (!silent && __instance.Type == PileType.Deck)
            ActivityCapture.Record(card.Owner, "obtain", () => card.Title, ActivityCapture.CardColor(card.Rarity));
    }
}
