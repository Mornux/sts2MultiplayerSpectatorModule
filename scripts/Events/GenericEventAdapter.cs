using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 通用只读采集：每个字段独立降级，模型不可读时只取玩家端已显示的文字。
// Capture read-only fields independently, falling back to rendered local text when model data is unavailable.
internal static class GenericEventAdapter
{
    private static T Read<T>(Func<T> read, T fallback)
    {
        try { return read(); } catch { return fallback; }
    }
    private static string Limit(string? text, int max) => text == null ? "" : text.Length > max ? text[..max] : text;
    private static string Format(Func<MegaCrit.Sts2.Core.Localization.LocString?> text, EventModel model, int max) =>
        Limit(Read(() => SpectatorEvents.Format(text(), model), ""), max);
    private static string Text(Node? root, params string[] paths)
    {
        foreach (string path in paths)
        {
            var node = Read(() => root?.GetNodeOrNull(path), null);
            string value = node switch { RichTextLabel rich => rich.Text, Label label => label.Text, _ => "" };
            if (value.Length > 0) return value;
        }
        return "";
    }

    internal static EventPlayerState Capture(EventModel model, bool local, string name, string[] choices)
    {
        ulong id = model.Owner!.NetId;
        Node? root = local ? Read(() => model.Node, null) : null;
        string title = Format(() => model.Title, model, 256);
        string description = Format(() => model.Description, model, 16000);
        if (title.Length == 0) title = Limit(Text(root, "%Title", "VBoxContainer/Title"), 256);
        if (title.Length == 0) title = model.Id.Entry;
        if (description.Length == 0) description = Limit(Text(root, "%EventDescription", "VBoxContainer/EventDescription"), 16000);
        var options = new List<EventOptionState>();
        foreach (var option in Read(() => model.CurrentOptions.ToArray(), []).Take(16))
        {
            string optionTitle = Format(() => option.Title, model, 1024);
            string optionDescription = Format(() => option.Description, model, 4000);
            options.Add(new(Limit(Read(() => option.TextKey, ""), 256), optionTitle, optionDescription,
                Read(() => option.IsLocked, true), Limit(Read(() => option.Relic?.Icon.ResourcePath, ""), 512)));
        }
        // 只回收选项区域中可见控件的文字，不复制信号或调用选项回调。
        // Recover visible option text only; never copy signals or invoke option callbacks.
        var container = Read(() => root?.GetNodeOrNull<Control>("%OptionsContainer") ?? root?.GetNodeOrNull<Control>("VBoxContainer/OptionsContainer"), null);
        if (container != null)
        {
            var buttons = container.GetChildren().OfType<Control>().Where(button => button.Visible).Take(16).ToArray();
            for (int index = 0; index < buttons.Length; index++)
            {
                string text = Limit(Text(buttons[index], "%Text", "Text", "Label"), 1024);
                if (text.Length == 0) continue;
                if (index >= options.Count) options.Add(new("visible." + index, text, "", true));
                else if (options[index].Title.Length == 0 && options[index].Description.Length == 0)
                    options[index] = options[index] with { Title = text };
            }
        }
        bool shared = Read(() => model.IsShared, false);
        int vote = shared ? Read(() => RunManager.Instance.EventSynchronizer.Events.Contains(model)
            ? (int?)RunManager.Instance.EventSynchronizer.GetPlayerVote(model.Owner) ?? -1 : -1, -1) : -1;
        string portrait = Limit(Read(() => root?.GetNodeOrNull<TextureRect>("%Portrait")?.Texture?.ResourcePath, ""), 512);
        return new(id, Limit(name, 40), model.Id.ToString(), title,
            description.Length > 0 ? description : L.Wire("events.updating"), Read(() => model.IsFinished, false),
            shared, options.ToArray(), choices.TakeLast(32).Select(choice => Limit(choice, 1024)).ToArray(),
            vote is >= 0 and < 16 ? vote : -1, portrait, LocalPage: local);
    }
}
