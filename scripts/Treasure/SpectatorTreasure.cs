using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts.Treasure;

// Observe the host's actual relics, votes and allocation; never enter a treasure room on the viewer.
public partial class SpectatorTreasure : Node
{
    internal static SpectatorTreasure? Instance { get; private set; }
    private SpectatorSession _session = null!;
    private SpectatorMap _map = null!;
    private SpectatorCombat _combat = null!;
    private SpectatorTreasureView _view = null!;
    private readonly BattleAssembler _assembler = new();
    private TreasureRoomRelicSynchronizer? _source;
    private AbstractRoom? _room;
    private TreasureRelicState[] _relics = [];
    private RelicModel[] _relicModels = [];
    private TreasureAwardState[] _awards = [];
    private TreasurePlayerState[] _players = [];
    private TreasureState? _state;
    private string _key = "", _last = "", _receivedKey = "";
    private long _sequence, _receivedSequence = -1;
    private ulong _viewers;
    private double _tick, _time, _sent;
    private string? _warning;
    private bool _opened;
    private bool? _mapOverride;

    public void Initialize(SpectatorSession session, SpectatorMap map, SpectatorCombat combat)
    {
        Instance = this; _session = session; _map = map; _combat = combat;
        _view = new SpectatorTreasureView();
        map.AddChild(_view); _view.Hide();
        session.BattleReceived += Receive;
    }

    public override void _Process(double delta)
    {
        if (_session == null) return;
        _time += delta;
        _assembler.Expire(Time.GetTicksMsec());
        if (_state is { } received && (!_session.Watching || !received.Active
            || _session.Map is not { } snapshotMap || received.Map != snapshotMap.Key || received.Floor != snapshotMap.Floor
            || snapshotMap.Room != nameof(RoomType.Treasure))) ClearState();
        var state = _state;
        bool available = _session.Watching && state is { Active: true }
            && _session.Map is { } map && state.Map == map.Key && state.Floor == map.Floor
            && map.Room == nameof(RoomType.Treasure) && !_combat.HasCombat;
        _map.UpdateRoomContent(_view, available && !(_mapOverride ?? state!.MapOpen), 0, available);
        if (!_session.Watching) { _receivedKey = ""; _receivedSequence = -1; _mapOverride = null; _assembler.Clear(); }
        try
        {
            var source = !_session.Watching && !_session.ReplayRunning && RunManager.Instance.IsInProgress
                ? RunManager.Instance.TreasureRoomRelicSynchronizer : null;
            if (!ReferenceEquals(source, _source))
            {
                if (_source != null) _source.RelicsAwarded -= Awarded;
                _source = source;
                if (_source != null) _source.RelicsAwarded += Awarded;
            }
            if ((_tick += delta) < 0.25) return;
            _tick = 0;
            if (!_session.IsHost || _session.ReplayRunning || _source == null) return;
            var run = RunManager.Instance.DebugOnlyGetState();
            if (run == null) return;
            ObserveRoom(run.BaseRoom);
            bool active = run.CurrentRoom is TreasureRoom;
            var current = active ? _source.CurrentRelics : null;
            if (current != null)
                CaptureRelics(current);
            CapturePlayers(run, current != null);
            var snapshot = new TreasureState(_key, _session.CurrentMapKey, run.TotalFloor, run.Act.Id.ToString(),
                active, active && (_opened || _awards.Length > 0) && current == null,
                NMapScreen.Instance is { IsOpen: true, IsTravelEnabled: true }, _relics, _players, _awards);
            if (!_session.HasViewer || !TreasureStateBounds.Valid(snapshot)) return;
            string json = JsonSerializer.Serialize(snapshot, SpectatorSession.WireJson);
            if (_viewers == _session.ViewerRevision && json == _last && _time - _sent < 1) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            if (bytes.Length > TreasureStateBounds.MaxBytes) return;
            _session.SendFragments("treasure-view", _key, _sequence, bytes);
            _sequence++; _last = json; _viewers = _session.ViewerRevision; _sent = _time;
        }
        catch (Exception error) { Warn(error); }
    }

    private static string Limit(string text, int max) => text.Length > max ? text[..max] : text;

    private void ObserveRoom(AbstractRoom? room)
    {
        if (ReferenceEquals(_room, room)) return;
        _room = room; _key = Guid.NewGuid().ToString("N");
        _relics = []; _relicModels = []; _awards = []; _players = []; _last = ""; _sequence = 0; _warning = null; _opened = false;
    }

    internal void HostOpened()
    {
        try
        {
            if (!_session.IsHost || _session.ReplayRunning || RunManager.Instance.DebugOnlyGetState()?.BaseRoom is not TreasureRoom room) return;
            ObserveRoom(room);
            _opened = true;
        }
        catch (Exception error) { Warn(error); }
    }

    private void CaptureRelics(IReadOnlyList<RelicModel> relics)
    {
        _relicModels = relics.Take(16).ToArray();
        _relics = _relicModels.Select(relic => new TreasureRelicState(relic.Id.ToString())).ToArray();
    }

    private void CapturePlayers(RunState run, bool voting)
    {
        _players = run.Players.Take(4).Select(player =>
        {
            var vote = voting ? _source?.GetPlayerVote(player) : null;
            var previous = _players.FirstOrDefault(item => item.Id == player.NetId);
            return new TreasurePlayerState(player.NetId, Limit(_session.PlayerName(player.NetId), 40), player.Character.Id.ToString(),
                vote?.voteReceived ?? previous?.Voted ?? false, vote != null ? vote.index ?? -1 : previous?.Vote ?? -1);
        }).ToArray();
    }

    private void Awarded(List<RelicPickingResult> results)
    {
        try
        {
            if (!_session.IsHost || _session.ReplayRunning || RunManager.Instance.DebugOnlyGetState()?.BaseRoom is not TreasureRoom room) return;
            ObserveRoom(room);
            // Preserve the final votes before EndRelicVoting clears CurrentRelics.
            if (_source?.CurrentRelics is { } relics) CaptureRelics(relics);
            else if (_relicModels.Length == 0) CaptureRelics(results.Select(result => result.relic).ToArray());
            CapturePlayers(RunManager.Instance.DebugOnlyGetState()!, _source?.CurrentRelics != null);
            _awards = results.Select(result => new TreasureAwardState(
                Array.FindIndex(_relicModels, relic => ReferenceEquals(relic, result.relic)), (int)result.type, result.player?.NetId ?? 0,
                result.fight?.playersInvolved.Select(player => player.NetId).ToArray() ?? [],
                result.fight?.rounds.Select(round => round.moves.Select(move => move.HasValue ? (int?)move.Value : null).ToArray()).ToArray() ?? [])).ToArray();
        }
        catch (Exception error) { Warn(error); }
    }

    private void Receive(string json)
    {
        if (!_session.Watching) return;
        try
        {
            var frame = JsonSerializer.Deserialize<BattleFrame>(json);
            if (frame?.Kind != "treasure-view" || frame.Parts > 22) return;
            var bytes = _assembler.Accept(frame, Time.GetTicksMsec());
            if (bytes == null || bytes.Length > TreasureStateBounds.MaxBytes) return;
            var state = JsonSerializer.Deserialize<TreasureState>(bytes);
            if (!TreasureStateBounds.Valid(state) || state!.Key != frame.Combat
                || state.Map != _session.Map?.Key || state.Floor != _session.Map.Floor
                || _receivedKey == state.Key && frame.Sequence <= _receivedSequence) return;
            if (_receivedKey != state.Key) _mapOverride = null;
            _receivedKey = state.Key; _receivedSequence = frame.Sequence; _state = state;
            if (state.Active) _view.Render(state); else ClearState();
        }
        catch (Exception error) { Warn(error); }
    }

    private void Warn(Exception error)
    {
        if (_warning == error.Message) return;
        _warning = error.Message;
        GD.PushWarning("[Spectator] Treasure: " + error.Message);
    }

    private void ClearState()
    {
        _state = null; _mapOverride = null;
        _view.Clear();
    }

    public override void _Input(InputEvent input)
    {
        if (!_session.Watching || _state is not { Active: true } state || _session.Map is not { } map
            || state.Map != map.Key || state.Floor != map.Floor || map.Room != nameof(RoomType.Treasure)
            || _combat.HasCombat || _map.IsPaused || SpectatorComment.IsEditing || !_map.OwnsRoomInput(_view)) return;
        if (input.IsActionPressed(MegaCrit.Sts2.Core.ControllerInput.MegaInput.viewMap) && !input.IsEcho())
        { _mapOverride = !(_mapOverride ?? state.MapOpen); GetViewport().SetInputAsHandled(); }
    }

    public override void _ExitTree()
    {
        if (_source != null) _source.RelicsAwarded -= Awarded;
        if (_session != null) _session.BattleReceived -= Receive;
        if (GodotObject.IsInstanceValid(_map) && GodotObject.IsInstanceValid(_view))
            _map.UpdateRoomContent(_view, false);
        if (GodotObject.IsInstanceValid(_view)) _view.QueueFree();
        if (Instance == this) Instance = null;
    }
}
