namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 只传输只读文字和展示资源，不传输任何可执行的事件操作。
// Transfer display text and visual resources, never executable event actions.
internal sealed record EventOptionState(string Key, string Title, string Description, bool Locked, string Relic = "");
internal sealed record EventPlayerState(ulong Id, string Name, string EventId, string Title, string Description,
    bool Finished, bool Shared, EventOptionState[] Options, string[] Choices, int Vote = -1,
    string Portrait = "", string Details = "", string[]? Cells = null, bool LocalPage = false);
internal sealed record EventState(string Key, string Map, int Floor, EventPlayerState[] Players, bool InCombat = false);
internal sealed record EventPeerState(string EventId, int Floor, EventPlayerState Player);

internal static class EventStateBounds
{
    internal static bool Valid(EventPlayerState? player) => player is { Name.Length: <= 40, EventId.Length: > 0 and <= 128,
        Title.Length: <= 256, Description.Length: <= 16000, Options.Length: <= 16, Choices.Length: <= 32,
        Details.Length: <= 12000, Vote: >= -1 and < 16, Portrait.Length: <= 512 }
        && player.Options.All(option => option is { Key.Length: <= 256, Title.Length: <= 1024,
            Description.Length: <= 4000, Relic.Length: <= 512 })
        && player.Choices.All(choice => choice is { Length: <= 1024 })
        && (player.Cells == null || player.Cells.Length == 11 && player.Cells.All(row => row is { Length: 11 }
            && row.All(cell => cell is '#' or '.' or '+')));
    internal static bool Valid(EventState? state) => state is { Key.Length: 32, Map.Length: 32, Floor: >= 0, Players.Length: <= 4 }
        && state.Players.All(Valid) && state.Players.Select(player => player.Id).Distinct().Count() == state.Players.Length;
}
