using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Models;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 先古的选项、遗物图标和已显示对话可读，观众不会推进对话。
// Read Ancient options, relic icons, and displayed dialogue without advancing it.
internal static class AncientEvents
{
    internal static EventPlayerState Enrich(AncientEventModel model, EventPlayerState state, bool local)
    {
        var dialogue = local ? model.Node?.GetNodeOrNull<Control>("%DialogueContainer") : null;
        string text = dialogue == null ? L.Wire("events.ancient")
            : string.Join("\n", Descendants(dialogue).OfType<MegaRichTextLabel>().Where(label => label.IsVisibleInTree()).Select(label => label.Text));
        return state with { Details = text };
    }
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
