using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;

namespace sts2MultiplayerSpectatorModule.Scripts.Treasure;

internal sealed record TreasureRelicState(string Id);
internal sealed record TreasurePlayerState(ulong Id, string Name, string CharacterId, bool Voted, int Vote);
internal sealed record TreasureAwardState(int Index, int Type, ulong Player, ulong[] Fighters, int?[][] Rounds);
internal sealed record TreasureState(string Key, string Map, int Floor, string ActId, bool Active, bool Finished, bool MapOpen,
    TreasureRelicState[] Relics, TreasurePlayerState[] Players, TreasureAwardState[] Awards);

internal static class TreasureStateBounds
{
    internal const int MaxBytes = 256000;
    internal static bool Valid(TreasureState? state) => state is
        { Key.Length: 32, Map.Length: 32, Floor: >= 0, ActId.Length: > 0 and <= 128,
            Relics.Length: <= 16, Players.Length: > 0 and <= 4, Awards.Length: <= 16 }
        && state.Relics.All(relic => relic is { Id.Length: > 0 and <= 128 })
        && state.Players.All(player => player is { Name.Length: <= 40, CharacterId.Length: > 0 and <= 128, Vote: >= -1 and < 16 }
            && !player.Name.Any(char.IsControl) && (!player.Voted || player.Vote < state.Relics.Length))
        && state.Players.Select(player => player.Id).Distinct().Count() == state.Players.Length
        && state.Awards.Select(award => award?.Index).Distinct().Count() == state.Awards.Length
        && state.Awards.All(award => ValidAward(award, state));

    private static bool ValidAward(TreasureAwardState? award, TreasureState state)
    {
        if (award is not { Index: >= 0, Fighters.Length: <= 4, Rounds.Length: <= 128 }
            || award.Index >= state.Relics.Length || !Enum.IsDefined((RelicPickingResultType)award.Type)) return false;
        if ((RelicPickingResultType)award.Type == RelicPickingResultType.Skipped)
            return award.Player == 0 && award.Fighters.Length == 0 && award.Rounds.Length == 0;
        if (!state.Players.Any(player => player.Id == award.Player)) return false;
        if ((RelicPickingResultType)award.Type != RelicPickingResultType.FoughtOver)
            return award.Fighters.Length == 0 && award.Rounds.Length == 0;
        return award.Fighters.Length >= 2 && award.Rounds.Length > 0
            && award.Fighters.Distinct().Count() == award.Fighters.Length && award.Fighters.Contains(award.Player)
            && award.Fighters.All(id => state.Players.Any(player => player.Id == id))
            && award.Rounds.All(round => round != null && round.Length == award.Fighters.Length
                && round.Any(move => move.HasValue) && round.All(move => !move.HasValue || Enum.IsDefined((RelicPickingFightMove)move.Value)))
            && award.Rounds[^1][Array.IndexOf(award.Fighters, award.Player)].HasValue;
    }
}
