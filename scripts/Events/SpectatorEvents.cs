using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 聚合玩家实际页面，原版模型作为后备；观众端不执行事件。
// Aggregate actual participant pages with model fallbacks, without executing events on the viewer.
public partial class SpectatorEvents : Node
{
    internal static SpectatorEvents? Instance { get; private set; }
    private SpectatorSession _session = null!;
    private SpectatorMap _map = null!;
    private SpectatorCombat _combat = null!;
    private SpectatorEventView _view = null!;
    private readonly BattleAssembler _assembler = new();
    private readonly Dictionary<ulong, List<string>> _choices = new();
    private readonly Dictionary<ulong, (EventPeerState State, double Seen)> _peers = new();
    private readonly Dictionary<ulong, BattleAssembler> _peerAssemblers = new();
    private EventContext? _context;
    private object? _localInstance;
    private string _localInstanceId = Guid.NewGuid().ToString("N"), _lastLocal = "";
    private long _localSequence;
    private double _contextSent, _localSent;
    private AbstractRoom? _room;
    private string _key = Guid.NewGuid().ToString("N"), _last = "", _receivedKey = "";
    private long _sequence, _receivedSequence;
    private double _time, _tick, _sent;
    private ulong _viewer;
    private EventState? _state;
    private bool _showMap;
    private readonly HashSet<string> _warnings = new();

    public void Initialize(SpectatorSession session, SpectatorMap map, SpectatorCombat combat)
    {
        Instance = this; _session = session; _map = map; _combat = combat;
        _view = new SpectatorEventView();
        map.AddChild(_view);
        _view.Hide();
        session.BattleReceived += Receive;
        session.EventPeerReceived += ReceivePeer;
        session.EventContextReceived += ReceiveContext;
    }

    private bool CanCapture(EventSynchronizer? source = null) => _session != null
        && !_session.ReplayRunning && !_session.Watching
        && (source == null || ReferenceEquals(source, RunManager.Instance.EventSynchronizer));

    private void Warn(string operation, Exception error)
    {
        if (_warnings.Count < 64 && _warnings.Add(operation))
            GD.PushWarning("[Spectator] Event " + operation + ": " + error.Message);
    }

    internal void Begin(EventSynchronizer? source = null)
    {
        try
        {
            if (!CanCapture(source)) return;
            _room = RunManager.Instance.DebugOnlyGetState()?.BaseRoom;
            _key = Guid.NewGuid().ToString("N"); _sequence = 0; _last = "";
            _choices.Clear(); _choiceCaptures.Clear(); _peers.Clear(); _peerAssemblers.Clear(); _warnings.Clear();
            _context = null;
            _localInstance = null; _lastLocal = "";
        }
        catch (Exception error) { Warn("begin", error); }
    }

    internal static string Format(LocString? source, EventModel model)
    {
        if (source == null || !source.Exists()) return "";
        var text = new LocString(source.LocTable, source.LocEntryKey);
        text.AddVariablesFrom(source);
        model.Owner?.Character.AddDetailsTo(text);
        text.Add("IsMultiplayer", model.Owner?.RunState.Players.Count > 1);
        model.DynamicVars.AddTo(text);
        return text.GetFormattedText();
    }

    internal EventPlayerState Capture(EventModel model, bool local)
    {
        ulong id = 0;
        string eventId = "unknown", name = "";
        string[] choices = [];
        EventPlayerState? generic = null;
        try
        {
            var owner = model.Owner ?? throw new InvalidOperationException("Event owner is unavailable.");
            id = owner.NetId;
            eventId = model.Id.ToString();
            name = _session.PlayerName(id);
            choices = _choices.GetValueOrDefault(id)?.ToArray() ?? [];
            var page = GenericEventAdapter.Capture(model, local, owner, eventId, name, choices);
            if (!EventStateBounds.Valid(page)) throw new InvalidOperationException("Event page exceeds the spectator page limits.");
            generic = page;
            return EventAdapters.Enrich(model, generic, local, error => Warn("adapter " + eventId, error));
        }
        catch (Exception error)
        {
            Warn("page " + eventId, error);
            // Never access the failed model a second time while constructing the last-resort page.
            return generic ?? GenericEventAdapter.Unavailable(id, eventId, name, choices, local);
        }
    }

    public override void _Process(double delta)
    {
        try { ProcessEvents(delta); }
        catch (Exception error) { Warn("poll", error); }
    }

    private bool IsFinished(EventModel model)
    {
        try { return model.IsFinished; }
        catch (Exception error) { Warn("finished", error); return false; }
    }

    private EventModel[] NativeModels(RunState run)
    {
        if (run.BaseRoom is not EventRoom) return [];
        var models = RunManager.Instance.EventSynchronizer.Events.ToArray();
        return NMapScreen.Instance is { IsOpen: true, IsTravelEnabled: true } && models.All(IsFinished) ? [] : models;
    }

    private ProvidedEventPage? CaptureLocal(RunState run, EventModel[] models)
    {
        var player = run.Players.FirstOrDefault(player => player.NetId == RunManager.Instance.NetService.NetId);
        if (player == null) return null;
        var model = models.FirstOrDefault(model => model.Owner?.NetId == player.NetId);
        return EventPageProviders.Capture(new(run, player, model), _session.PlayerName(player.NetId))
            ?? (model == null ? null : new ProvidedEventPage(model, Capture(model, true)));
    }

    private EventPlayerState[] CapturePlayers(RunState run, EventModel[] models, ProvidedEventPage? local)
    {
        var pages = new List<EventPlayerState>();
        ulong localId = RunManager.Instance.NetService.NetId;
        foreach (var player in run.Players.Take(4))
        {
            try
            {
                if (player.NetId == localId)
                {
                    if (local != null) pages.Add(local.Page);
                    continue;
                }
                var model = models.FirstOrDefault(model => model.Owner?.NetId == player.NetId);
                var fallback = model == null ? null : Capture(model, false);
                if (_peers.TryGetValue(player.NetId, out var peer))
                {
                    var page = peer.State.Player;
                    if (page != null && (page.Source == EventPageSource.Provider || model != null && page.EventId == model.Id.ToString()))
                    {
                        if (_time - peer.Seen < 3) { pages.Add(page); continue; }
                        // Keep known contents explicitly stale rather than silently claiming a live page.
                        if (fallback == null) { pages.Add(page with { Stale = true }); continue; }
                    }
                }
                if (fallback != null) pages.Add(fallback);
            }
            catch (Exception error) { Warn("player page", error); }
        }
        return pages.ToArray();
    }

    private void ProcessEvents(double delta)
    {
        _time += delta;
        _assembler.Expire(Time.GetTicksMsec());
        foreach (var assembler in _peerAssemblers.Values) assembler.Expire(Time.GetTicksMsec());
        bool current = _state != null && _state.Map == _session.Map?.Key && _state.Floor == _session.Map.Floor
            && _state.Room == _session.Map.Room;
        bool available = _session.Watching && current && _state!.Players.Length > 0 && !_state.InCombat && !_combat.HasCombat;
        int priority = _state?.Players.Any(player => player.Source == EventPageSource.Provider) == true ? 100 : 10;
        _map.UpdateRoomContent(_view, available && !_showMap, priority, available);
        if (!_session.Watching) { _state = null; _receivedKey = ""; _receivedSequence = 0; }
        if ((_tick += delta) < 0.25) return;
        _tick = 0;
        if (_session.Watching || _session.ReplayRunning || !RunManager.Instance.IsInProgress) return;
        var run = RunManager.Instance.DebugOnlyGetState();
        if (run == null) return;
        if (!ReferenceEquals(_room, run.BaseRoom)) Begin();
        var models = NativeModels(run);
        var local = CaptureLocal(run, models);
        if (!_session.IsHost)
        {
            if (_session.DisplayViewerId != 0 && _context is { } context && context.Floor == run.TotalFloor)
                SendLocalPage(context, local);
            return;
        }
        if (!_session.HasViewer) { _context = null; _peers.Clear(); return; }
        if (_context == null || _context.Map != _session.CurrentMapKey || _context.Floor != run.TotalFloor)
        {
            _key = Guid.NewGuid().ToString("N");
            _context = new(_key, _session.CurrentMapKey, run.TotalFloor);
            _peers.Clear(); _peerAssemblers.Clear(); _contextSent = -10;
        }
        if (_time - _contextSent >= 1)
        {
            _session.SendEventContext(JsonSerializer.Serialize(_context));
            _contextSent = _time;
        }
        var players = CapturePlayers(run, models, local);
        var state = new EventState(_context.Key, _session.CurrentMapKey, run.TotalFloor, players, run.CurrentRoom is CombatRoom,
            run.CurrentRoom?.RoomType.ToString() ?? "Map");
        string json = JsonSerializer.Serialize(state, SpectatorSession.WireJson);
        if (_viewer == _session.ViewerRevision && json == _last && _time - _sent < 1) return;
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        if (bytes.Length > BattleAssembler.MaxBytes) { Warn("state size", new InvalidOperationException("Event pages exceed the snapshot limit.")); return; }
        _session.SendFragments("event-view", _context.Key, _sequence, bytes);
        _viewer = _session.ViewerRevision; _last = json; _sent = _time;
        _sequence++;
    }

    private void SendLocalPage(EventContext context, ProvidedEventPage? local)
    {
        if (!ReferenceEquals(_localInstance, local?.Instance))
        {
            _localInstance = local?.Instance;
            _localInstanceId = Guid.NewGuid().ToString("N");
            _lastLocal = "";
        }
        string contents = JsonSerializer.Serialize(local?.Page, SpectatorSession.WireJson);
        if (contents == _lastLocal && _time - _localSent < 1) return;
        long sequence = _localSequence + 1;
        var page = new EventPeerState(context.Key, _localInstanceId, sequence, context.Map, context.Floor, local?.Page);
        string json = JsonSerializer.Serialize(page, SpectatorSession.WireJson);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > EventStateBounds.MaxPageBytes)
        { Warn("local page size", new InvalidOperationException("Local event page exceeds the transfer limit.")); return; }
        _session.SendEventPeer(json, context.Key, sequence);
        _localSequence = sequence; _lastLocal = contents; _localSent = _time;
    }

    private void ReceiveContext(string json)
    {
        try
        {
            var context = JsonSerializer.Deserialize<EventContext>(json);
            if (context is not { Key.Length: 32, Map.Length: 32, Floor: >= 0 } || _session.Watching || _session.ReplayRunning) return;
            var run = RunManager.Instance.DebugOnlyGetState();
            if (run == null || context.Floor != run.TotalFloor) return;
            if (_context?.Key != context.Key) { _lastLocal = ""; _localSequence = 0; }
            _context = context;
        }
        catch (JsonException) { }
    }

    private void ReceivePeer(ulong sender, string json)
    {
        try
        {
            if (_context == null) return;
            var frame = JsonSerializer.Deserialize<BattleFrame>(json);
            if (frame is not { Kind: "event-local" } || frame.Combat != _context.Key || frame.Parts > 22) return;
            if (!_peerAssemblers.TryGetValue(sender, out var assembler))
            {
                if (_peerAssemblers.Count >= 4) return;
                _peerAssemblers[sender] = assembler = new();
            }
            var bytes = assembler.Accept(frame, Time.GetTicksMsec());
            if (bytes == null || bytes.Length > EventStateBounds.MaxPageBytes) return;
            var page = JsonSerializer.Deserialize<EventPeerState>(bytes);
            if (page?.Sequence != frame.Sequence) return;
            AcceptPeerPage(sender, page);
        }
        catch (Exception error) { Warn("peer page", error); }
    }

    internal bool AcceptPeerPage(ulong sender, EventPeerState? state)
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (_context == null || run == null || !run.Players.Any(player => player.NetId == sender)
            || state is not { Instance.Length: 32, Sequence: > 0 } || state.Context != _context.Key
            || state.Map != _context.Map || state.Floor != _context.Floor
            || state.Player != null && (state.Player.Id != sender || !EventStateBounds.Valid(state.Player))
            || _peers.TryGetValue(sender, out var previous) && previous.State.Sequence >= state.Sequence) return false;
        if (state.Player is { } player)
            state = state with { Player = player with { Name = _session.PlayerName(sender), LocalPage = true, Stale = false } };
        _peers[sender] = (state, _time);
        _last = "";
        return true;
    }

    private void Receive(string json)
    {
        if (!_session.Watching) return;
        try
        {
            var frame = JsonSerializer.Deserialize<BattleFrame>(json);
            if (frame?.Kind != "event-view" || frame.Parts > 175) return;
            var bytes = _assembler.Accept(frame, Time.GetTicksMsec());
            if (bytes == null) return;
            var state = JsonSerializer.Deserialize<EventState>(bytes);
            if (!EventStateBounds.Valid(state) || state!.Map != _session.Map?.Key || state.Floor != _session.Map.Floor || state.Key != frame.Combat
                || _receivedKey == state.Key && frame.Sequence <= _receivedSequence) return;
            if (_receivedKey != state.Key) _showMap = false;
            _receivedKey = state.Key; _receivedSequence = frame.Sequence; _state = state;
            _view.Render(state);
        }
        catch (Exception error) { GD.PushWarning("[Spectator] Event snapshot: " + error.Message); }
    }

    public override void _Input(InputEvent input)
    {
        if (!_session.Watching || _state?.Players.Length is not > 0 || _state.Map != _session.Map?.Key
            || _state.Floor != _session.Map.Floor || _state.Room != _session.Map.Room || _state.InCombat
            || !_map.OwnsRoomInput(_view) || _combat.HasCombat || SpectatorComment.IsEditing || _map.IsPaused) return;
        if (input.IsActionPressed(MegaCrit.Sts2.Core.ControllerInput.MegaInput.viewMap) && !input.IsEcho())
        { _showMap = !_showMap; GetViewport().SetInputAsHandled(); }
        else if (_view.IsVisibleInTree() && _map.CurrentScreen == _view
            && input is InputEventMouseButton { Pressed: true } mouse
            && mouse.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && !_view.IsScrollingContent(mouse.Position))
        {
            _view.SwitchPlayer(mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
            GetViewport().SetInputAsHandled();
        }
    }
    public override void _ExitTree()
    {
        if (_session == null) return;
        _session.BattleReceived -= Receive; _session.EventPeerReceived -= ReceivePeer;
        _session.EventContextReceived -= ReceiveContext;
        if (GodotObject.IsInstanceValid(_map) && GodotObject.IsInstanceValid(_view)) _map.UpdateRoomContent(_view, false);
        if (GodotObject.IsInstanceValid(_view)) _view.QueueFree();
        if (Instance == this) Instance = null;
    }
}
