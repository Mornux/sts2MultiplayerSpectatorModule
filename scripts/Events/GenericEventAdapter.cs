using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 通用只读采集优先采用本地整页选项，避免按位置混合模型与界面。
// Prefer whole displayed option pages without joining model and UI arrays by index.
internal static class GenericEventAdapter
{
    private static T Read<T>(Func<T> read, T fallback)
    {
        try { return read(); } catch { return fallback; }
    }
    private static string Limit(string? text, int max) => text == null ? "" : text.Length > max ? text[..max] : text;
    internal static EventPlayerState Unavailable(ulong id, string eventId, string name, string[] choices, bool local) =>
        new(id, Limit(name, 40), string.IsNullOrEmpty(eventId) ? "unknown" : Limit(eventId, 128), Limit(eventId, 256), L.Wire("events.updating"), false, false,
            [], choices.TakeLast(32).Select(choice => Limit(choice, 1024)).ToArray(), LocalPage: local, Source: EventPageSource.Unavailable);
    private static string Format(Func<MegaCrit.Sts2.Core.Localization.LocString?> text, EventModel model, int max) =>
        Limit(Read(() => SpectatorEvents.Format(text(), model), ""), max);
    private static string Text(Node? root, params string[] paths)
    {
        foreach (string path in paths)
        {
            var node = Read(() => root?.GetNodeOrNull(path), null);
            string value = Read(() => node switch { RichTextLabel rich => rich.Text, Label label => label.Text, _ => "" }, "");
            if (value.Length > 0) return value;
        }
        return "";
    }

    private static IEnumerable<Node> VisibleNodes(Node root)
    {
        var pending = new Queue<Node>();
        pending.Enqueue(root);
        int visited = 0;
        while (pending.TryDequeue(out var node) && visited++ < 512)
        {
            if (node is CanvasItem { Visible: false }) continue;
            yield return node;
            foreach (var child in node.GetChildren()) pending.Enqueue(child);
        }
    }

    internal static EventPlayerState Capture(EventModel model, bool local, Player owner, string eventId, string name, string[] choices)
    {
        ulong id = owner.NetId;
        Node? root = local ? Read(() => model.Node, null) : null;
        string title = Format(() => model.Title, model, 256);
        string description = Format(() => model.Description, model, 16000);
        var nativeOptions = Read(() => model.CurrentOptions.ToArray(), []);
        var options = nativeOptions.Take(EventStateBounds.MaxOptions).Select(option => new EventOptionState(
            Limit(Read(() => option.TextKey, ""), 256), Format(() => option.Title, model, 1024),
            Format(() => option.Description, model, 4000), Read(() => option.IsLocked, true),
            Limit(Read(() => option.Relic?.Icon.ResourcePath, ""), 512))).ToArray();
        var source = EventPageSource.Model;
        if (root != null && GodotObject.IsInstanceValid(root) && root is not CanvasItem { Visible: false })
        {
            string visibleTitle = Text(root, "%Title", "VBoxContainer/Title");
            string visibleBody = Text(root, "%EventDescription", "VBoxContainer/EventDescription");
            if (visibleTitle.Length > 0) title = Limit(visibleTitle, 256);
            if (visibleBody.Length > 0) description = Limit(visibleBody, 16000);
            var buttons = VisibleNodes(root).OfType<NEventOptionButton>()
                .Where(button => ReferenceEquals(button.Event, model)).Take(EventStateBounds.MaxOptions).ToArray();
            if (buttons.Length > 0 || root is NEventLayout)
            {
                // Native button text already includes its formatted title and description.
                options = buttons.Select(button => new EventOptionState(
                    Limit(Read(() => button.Option.TextKey, ""), 256), "",
                    Limit(Text(button, "%Text", "Text", "Label"), 4000),
                    Read(() => button.Option.IsLocked, true), Limit(Read(() => button.Option.Relic?.Icon.ResourcePath, ""), 512))).ToArray();
                source = EventPageSource.Visible;
            }
            else
            {
                // Limit unknown layouts to their explicit option container, not every label in the scene.
                var container = Read(() => root.GetNodeOrNull<Control>("%OptionsContainer")
                    ?? root.GetNodeOrNull<Control>("VBoxContainer/OptionsContainer"), null);
                if (container is { Visible: true })
                {
                    options = container.GetChildren().OfType<Control>().Where(button => button.Visible)
                        .Take(EventStateBounds.MaxOptions).Select((button, index) => new EventOptionState("visible." + index,
                            Limit(Text(button, "%Text", "Text", "Label"), 1024), "", true)).Where(option => option.Title.Length > 0).ToArray();
                    source = EventPageSource.Visible;
                }
            }
        }
        if (title.Length == 0) title = Limit(Read(() => model.Id.Entry, eventId), 256);
        bool shared = Read(() => model.IsShared, false);
        int vote = shared ? Read(() => RunManager.Instance.EventSynchronizer.Events.Contains(model)
            ? (int?)RunManager.Instance.EventSynchronizer.GetPlayerVote(owner) ?? -1 : -1, -1) : -1;
        string portrait = Limit(Read(() => root?.GetNodeOrNull<TextureRect>("%Portrait")?.Texture?.ResourcePath, ""), 512);
        if (portrait.Length == 0)
            portrait = Limit(Read(() => model.LayoutType == MegaCrit.Sts2.Core.Events.EventLayoutType.Default
                ? model.CreateInitialPortrait()?.ResourcePath : "", ""), 512);
        return new(id, Limit(name, 40), eventId, title,
            description.Length > 0 ? description : L.Wire("events.updating"), Read(() => model.IsFinished, false),
            shared, options, choices.TakeLast(32).Select(choice => Limit(choice, 1024)).ToArray(),
            vote >= 0 && vote < EventStateBounds.MaxOptions ? vote : -1, portrait, LocalPage: local, Source: source,
            VoteKey: source == EventPageSource.Model || options.Any(option => !option.Key.StartsWith("visible."))
                ? Limit(vote >= 0 && vote < nativeOptions.Length ? Read(() => nativeOptions[vote].TextKey, "") : "", 256) : "");
    }
}
