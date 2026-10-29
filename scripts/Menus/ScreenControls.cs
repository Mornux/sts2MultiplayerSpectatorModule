using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 复用原版列表与页面控件。
// Reuse native list and screen controls.

public partial class SpectatorPanel
{

    private static NJoinFriendScreen CreateScreen(string name)
    {
        var screen = ResourceLoader.Load<PackedScene>("res://scenes/screens/join_friend_submenu.tscn")
            .Instantiate<NJoinFriendScreen>();
        screen.Name = name;
        screen.Visible = false;
        return screen;
    }

    private static void Push(NSubmenu source, NJoinFriendScreen screen)
    {
        var stack = (NSubmenuStack)AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(source)!;
        stack.AddChild(screen);
        stack.Push(screen);
    }

    private static Control ClearList(NJoinFriendScreen screen, string emptyText, bool empty)
    {
        var container = screen.GetNode<Control>("%ButtonContainer");
        foreach (var child in container.GetChildren()) { container.RemoveChild(child); child.QueueFree(); }
        var label = screen.GetNode<MegaLabel>("%NoFriendsText");
        label.SetTextAutoSize(emptyText);
        label.Visible = empty;
        return container;
    }

    private void AddFriend(Control container, ulong id, string status, Action action, string? displayName = null)
    {
        var button = NJoinFriendButton.Create(SpectatorLocal.IsLocal(id) ? 0 : id);
        container.AddChild(button);
        string name = (displayName ?? _session.PlayerName(id)).Replace("[", "[lb]");
        button.GetNode<MegaRichTextLabel>("%Text").Text = $"[center]{name}\n{status}[/center]";
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => action()));
    }
}
