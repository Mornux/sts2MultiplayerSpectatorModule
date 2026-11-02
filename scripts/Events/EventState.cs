namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 只传输只读文字和展示资源，不传输任何可执行的事件操作。
// Transfer display text and visual resources, never executable event actions.
public sealed record EventOptionState(string Key, string Title, string Description, bool Locked, string Relic = "");
public enum EventPageSource { Model, Visible, Provider, Unavailable }
public sealed record EventPlayerState(ulong Id, string Name, string EventId, string Title, string Description,
    bool Finished, bool Shared, EventOptionState[] Options, string[] Choices, int Vote = -1,
    string Portrait = "", string Details = "", string[]? Cells = null, bool LocalPage = false,
    EventPageSource Source = EventPageSource.Model, string VoteKey = "", bool Stale = false);
internal sealed record EventState(string Key, string Map, int Floor, EventPlayerState[] Players, bool InCombat = false, string Room = "Event");
internal sealed record EventContext(string Key, string Map, int Floor);
internal sealed record EventPeerState(string Context, string Instance, long Sequence, string Map, int Floor, EventPlayerState? Player);

internal static class EventStateBounds
{
    internal const int MaxOptions = 64, MaxPageBytes = 256000;
    internal static bool Valid(EventPlayerState? player) => player is { Name.Length: <= 40, EventId.Length: > 0 and <= 128,
        Title.Length: <= 256, Description.Length: <= 16000, Options.Length: <= MaxOptions, Choices.Length: <= 32,
        Details.Length: <= 12000, Vote: >= -1 and < MaxOptions, Portrait.Length: <= 512, VoteKey.Length: <= 256 }
        && Enum.IsDefined(player.Source)
        && player.Options.All(option => option is { Key.Length: <= 256, Title.Length: <= 1024,
            Description.Length: <= 4000, Relic.Length: <= 512 })
        && player.Choices.All(choice => choice is { Length: <= 1024 })
        && (player.Cells == null || player.Cells.Length == 11 && player.Cells.All(row => row is { Length: 11 }
            && row.All(cell => cell is '#' or '.' or '+')));
    internal static bool Valid(EventState? state) => state is { Key.Length: 32, Map.Length: 32, Floor: >= 0, Room.Length: > 0 and <= 128, Players.Length: <= 4 }
        && state.Players.All(Valid) && state.Players.Select(player => player.Id).Distinct().Count() == state.Players.Length;
}
