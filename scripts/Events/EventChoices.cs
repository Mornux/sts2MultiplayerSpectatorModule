using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

internal sealed record EventChoiceCapture(EventSynchronizer Source, Player Player, EventOption Option,
    string Title, bool Vote, bool Shared, string Visit)
{
    internal Task? Completion;
}

public partial class SpectatorEvents
{
    private readonly Dictionary<EventOption, EventChoiceCapture> _choiceCaptures = new();
    private static readonly Lazy<FieldInfo?> PageIndex = new(() => AccessTools.Field(typeof(EventSynchronizer), "_pageIndex"));

    // Capture the old page before native execution, but publish only after its operation succeeds.
    internal EventChoiceCapture? Chosen(EventSynchronizer source, Player player, int index, bool vote, uint? pageIndex = null)
    {
        try
        {
            if (!CanCapture(source)) return null;
            // Only resolve the optional private binding after checking the live event context.
            if (pageIndex.HasValue)
            {
                var field = PageIndex.Value;
                if (field == null || field.FieldType != typeof(uint)) throw new MissingFieldException(typeof(EventSynchronizer).FullName, "_pageIndex");
                if (field.GetValue(source) is not uint current || current != pageIndex.Value) return null;
            }
            var model = source.GetEventForPlayer(player);
            if (model.IsFinished || index < 0 || index >= model.CurrentOptions.Count) return null;
            var option = model.CurrentOptions[index];
            if (option.IsLocked || option.IsProceed) return null;
            var title = Format(option.Title, model);
            if (title.Length == 0) title = Format(option.Description, model);
            if (title.Length > 800) title = title[..800];
            var capture = new EventChoiceCapture(source, player, option, title, vote, model.IsShared, _key);
            if (!vote) _choiceCaptures[option] = capture;
            return capture;
        }
        catch (Exception error) { Warn("choice", error); }
        return null;
    }

    internal void ObserveChoiceTask(EventOption option, Task task)
    {
        if (_choiceCaptures.TryGetValue(option, out var capture)) capture.Completion = task;
    }

    internal void ReleaseChoice(EventChoiceCapture? capture)
    {
        if (capture != null && _choiceCaptures.TryGetValue(capture.Option, out var current) && ReferenceEquals(current, capture))
            _choiceCaptures.Remove(capture.Option);
    }

    internal void ConfirmChoice(EventChoiceCapture? capture)
    {
        ReleaseChoice(capture);
        if (capture == null) return;
        if (capture.Vote) RecordChoice(capture);
        else if (capture.Completion is { } completion) _ = ConfirmChoiceAfter(completion, capture);
    }

    private async Task ConfirmChoiceAfter(Task completion, EventChoiceCapture capture)
    {
        // Observe without replacing the task. Native RunSafely keeps its own exception reporting.
        try { await completion; }
        catch (Exception) { return; }
        RecordChoice(capture);
    }

    private void RecordChoice(EventChoiceCapture capture)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || !CanCapture(capture.Source) || capture.Visit != _key) return;
            if (!_choices.TryGetValue(capture.Player.NetId, out var choices)) _choices[capture.Player.NetId] = choices = new();
            if (choices.Count >= 32) choices.RemoveAt(0);
            choices.Add(L.Wire(capture.Vote ? "events.vote_prefix" : capture.Shared ? "events.final_prefix" : "events.choice_prefix") + capture.Title);
            if (capture.Vote || !capture.Shared) ActivityCapture.Record(capture.Player, "choice", () => capture.Title);
            _last = "";
        }
        catch (Exception error) { Warn("confirmed choice", error); }
    }

}

// 记录普通选择与共享投票，不改写同步器的结果。
// Observe individual choices and shared votes without changing synchronization.
[HarmonyPatch(typeof(EventSynchronizer), "BeginEvent")]
internal static class SpectatorEventBegin
{
    private static void Prefix(EventSynchronizer __instance)
    {
        SpectatorEvents.Instance?.Begin(__instance);
    }
}
[HarmonyPatch(typeof(EventSynchronizer), "ChooseOptionForEvent")]
internal static class SpectatorEventChoice
{
    private static void Prefix(EventSynchronizer __instance, Player player, int optionIndex, out EventChoiceCapture? __state) =>
        __state = SpectatorEvents.Instance?.Chosen(__instance, player, optionIndex, false);
    private static void Postfix(EventChoiceCapture? __state) => SpectatorEvents.Instance?.ConfirmChoice(__state);
    private static void Finalizer(EventChoiceCapture? __state) => SpectatorEvents.Instance?.ReleaseChoice(__state);
}
[HarmonyPatch(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex")]
internal static class SpectatorEventVote
{
    private static void Prefix(EventSynchronizer __instance, Player player, uint optionIndex, uint pageIndex, out EventChoiceCapture? __state) =>
        __state = SpectatorEvents.Instance?.Chosen(__instance, player, (int)optionIndex, true, pageIndex);
    private static void Postfix(EventChoiceCapture? __state) => SpectatorEvents.Instance?.ConfirmChoice(__state);
}

[HarmonyPatch(typeof(EventOption), nameof(EventOption.Chosen))]
internal static class SpectatorEventOptionTask
{
    private static void Postfix(EventOption __instance, Task __result) => SpectatorEvents.Instance?.ObserveChoiceTask(__instance, __result);
}
