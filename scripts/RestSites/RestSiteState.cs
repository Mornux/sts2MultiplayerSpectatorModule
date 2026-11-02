namespace sts2MultiplayerSpectatorModule.Scripts;

// Only already-generated options are transferred; no option actions cross the wire.
internal sealed record RestSiteOptionState(string Id, string Title, string Description, string Icon, bool Enabled);
internal sealed record RestSitePlayerState(ulong Id, string Name, string Character, RestSiteOptionState[] Options, string[] Choices);
internal sealed record RestSiteState(string Key, string Map, int Floor, int Act, string ActId, ulong HostId, RestSitePlayerState[] Players, bool MapOpen = false);

internal static class RestSiteBounds
{
    internal static bool Valid(RestSiteState? state) => state is
        { Key.Length: 32, Map.Length: 32, Floor: >= 0, Act: >= 0 and <= 100, ActId.Length: > 0 and <= 128, Players.Length: > 0 and <= 4 }
        && state.Players.All(player => player is
            { Id: not 0, Name.Length: <= 40, Character.Length: > 0 and <= 128, Options.Length: <= 32, Choices.Length: <= 32 }
            && player.Options.All(option => option is { Id.Length: > 0 and <= 128, Title.Length: <= 1024,
                Description.Length: <= 4000, Icon.Length: <= 512 })
            && player.Choices.All(choice => choice is { Length: <= 1024 }))
        && state.Players.Select(player => player.Id).Distinct().Count() == state.Players.Length
        && state.Players.Any(player => player.Id == state.HostId);
}
