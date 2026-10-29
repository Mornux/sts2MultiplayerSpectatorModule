using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
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

// 读取房主的所有事件实例，并接收玩家端实际页面的补充，不在观众端运行事件。
// Read host event instances plus actual participant pages without running events on the viewer.
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
    private AbstractRoom? _room;
    private string _key = Guid.NewGuid().ToString("N"), _last = "", _receivedKey = "";
    private long _sequence, _receivedSequence;
    private double _time, _tick, _sent;
    private ulong _viewer;
    private EventState? _state;
    private bool _showMap;

    public void Initialize(SpectatorSession session, SpectatorMap map, SpectatorCombat combat)
    {
        Instance = this; _session = session; _map = map; _combat = combat;
        _view = new SpectatorEventView();
        map.AddChild(_view);
        _view.Hide();
        session.BattleReceived += Receive;
        session.EventPeerReceived += ReceivePeer;
    }

    internal void Begin()
    {
        if (_session.ReplayRunning || _session.Watching) return;
        _room = RunManager.Instance.DebugOnlyGetState()?.BaseRoom;
        _key = Guid.NewGuid().ToString("N"); _sequence = 0; _last = "";
        _choices.Clear(); _peers.Clear();
    }

    // 在原版执行前记录文字，快速切页也不会丢失玩家选择。
    // Record text before native execution so fast page changes cannot hide choices.
    internal void Chosen(EventSynchronizer source, Player player, int index, bool vote)
    {
        try { RecordChoice(source, player, index, vote); }
        catch (Exception error) { GD.PushWarning("[Spectator] Event choice: " + error.Message); }
    }
    private void RecordChoice(EventSynchronizer source, Player player, int index, bool vote)
    {
        if (_session.ReplayRunning || _session.Watching || source != RunManager.Instance.EventSynchronizer) return;
        var model = source.GetEventForPlayer(player);
        if (index < 0 || index >= model.CurrentOptions.Count) return;
        var title = Format(model.CurrentOptions[index].Title, model);
        if (title.Length == 0) title = Format(model.CurrentOptions[index].Description, model);
        if (title.Length > 800) title = title[..800];
        if (!_choices.TryGetValue(player.NetId, out var choices)) _choices[player.NetId] = choices = new();
        if (choices.Count >= 32) choices.RemoveAt(0);
        choices.Add((vote ? L.Wire("events.vote_prefix") : model.IsShared ? L.Wire("events.final_prefix") : L.Wire("events.choice_prefix")) + title);
        if (vote || !model.IsShared)
            ActivityCapture.Record(player, "choice", () => title);
        _last = "";
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
        try { return CapturePage(model, local); }
        catch (Exception error)
        {
            GD.PushWarning("[Spectator] Event page " + model.Id + ": " + error.Message);
            return new(model.Owner!.NetId, _session.PlayerName(model.Owner.NetId), model.Id.ToString(), model.Id.Entry,
                L.Wire("events.updating"), model.IsFinished, model.IsShared, [],
                _choices.GetValueOrDefault(model.Owner.NetId)?.ToArray() ?? []);
        }
    }
    private EventPlayerState CapturePage(EventModel model, bool local)
    {
        var owner = model.Owner!;
        int vote = model.IsShared && RunManager.Instance.EventSynchronizer?.Events.Contains(model) == true
            ? (int?)RunManager.Instance.EventSynchronizer.GetPlayerVote(owner) ?? -1 : -1;
        var options = model.CurrentOptions.Take(16).Select(option => new EventOptionState(option.TextKey,
            Format(option.Title, model), Format(option.Description, model), option.IsLocked, option.Relic?.Icon.ResourcePath ?? "")).ToArray();
        string portrait = "";
        if (local && model.Node is MegaCrit.Sts2.Core.Nodes.Events.NEventLayout layout)
            portrait = layout.GetNodeOrNull<TextureRect>("%Portrait")?.Texture?.ResourcePath ?? "";
        var result = new EventPlayerState(owner.NetId, _session.PlayerName(owner.NetId), model.Id.ToString(),
            Format(model.Title, model), Format(model.Description, model), model.IsFinished, model.IsShared, options,
            _choices.GetValueOrDefault(owner.NetId)?.ToArray() ?? [], vote, portrait, LocalPage: local);
        return EventAdapters.Enrich(model, result, local);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _assembler.Expire(Time.GetTicksMsec());
        bool visible = _session.Watching && _state?.Players.Length > 0 && !_state.InCombat && !_combat.HasCombat && !_showMap;
        _map.SetEventContent(visible ? _view : null);
        if (!_session.Watching) { _state = null; _receivedKey = ""; _receivedSequence = 0; }
        if ((_tick += delta) < 0.25) return;
        _tick = 0;
        if (_session.Watching || _session.ReplayRunning || !RunManager.Instance.IsInProgress) return;
        var run = RunManager.Instance.DebugOnlyGetState();
        if (run == null) return;
        if (!ReferenceEquals(_room, run.BaseRoom)) Begin();
        if (!_session.IsHost && _session.DisplayViewerId != 0 && run.BaseRoom is EventRoom
            && RunManager.Instance.EventSynchronizer.Events.FirstOrDefault(model => model.Owner?.NetId == RunManager.Instance.NetService.NetId) is { } local)
        {
            string page = JsonSerializer.Serialize(new EventPeerState(local.Id.ToString(), run.TotalFloor, Capture(local, true)), SpectatorSession.WireJson);
            if (page != _last || _time - _sent >= 1) { _session.SendEventPeer(page); _last = page; _sent = _time; }
        }
        if (!_session.IsHost || !_session.HasViewer) return;
        bool active = run.BaseRoom is EventRoom && !(NMapScreen.Instance is { IsOpen: true, IsTravelEnabled: true }
            && RunManager.Instance.EventSynchronizer.Events.All(model => model.IsFinished));
        var players = active ? RunManager.Instance.EventSynchronizer.Events.Where(model => model.Owner != null).Take(4).Select(model =>
        {
            var player = Capture(model, model.Owner!.NetId == RunManager.Instance.NetService.NetId);
            if (_peers.TryGetValue(player.Id, out var peer) && _time - peer.Seen < 3
                && peer.State.Floor == run.TotalFloor && peer.State.EventId == player.EventId)
                player = peer.State.Player with { Name = player.Name, Choices = player.Choices, Vote = player.Vote };
            return player;
        }).ToArray() : [];
        var state = new EventState(_key, _session.CurrentMapKey, run.TotalFloor, players,
            run.CurrentRoom is not EventRoom);
        string json = JsonSerializer.Serialize(state, SpectatorSession.WireJson);
        if (_viewer == _session.ViewerId && json == _last && _time - _sent < 1) return;
        _viewer = _session.ViewerId; _last = json; _sent = _time;
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        if (bytes.Length > 256000) return;
        _session.SendFragments("event-view", _key, _sequence, bytes);
        _sequence++;
    }

    private void ReceivePeer(ulong sender, string json)
    {
        try
        {
            var state = JsonSerializer.Deserialize<EventPeerState>(json);
            if (state?.Player.Id == sender && state.EventId == state.Player.EventId && EventStateBounds.Valid(state.Player))
                _peers[sender] = (state with { Player = state.Player with { LocalPage = true } }, _time);
        }
        catch (JsonException) { }
    }

    private void Receive(string json)
    {
        if (!_session.Watching) return;
        try
        {
            var frame = JsonSerializer.Deserialize<BattleFrame>(json);
            if (frame?.Kind != "event-view" || frame.Parts > 22) return;
            var bytes = _assembler.Accept(frame, Time.GetTicksMsec());
            if (bytes == null) return;
            var state = JsonSerializer.Deserialize<EventState>(bytes);
            if (!EventStateBounds.Valid(state) || state!.Map != _session.Map?.Key || state.Floor < _session.Map.Floor || state.Key != frame.Combat
                || _receivedKey == state.Key && frame.Sequence <= _receivedSequence) return;
            if (_receivedKey != state.Key) _showMap = false;
            _receivedKey = state.Key; _receivedSequence = frame.Sequence; _state = state;
            _view.Render(state);
        }
        catch (Exception error) { GD.PushWarning("[Spectator] Event snapshot: " + error.Message); }
    }

    public override void _Input(InputEvent input)
    {
        if (!_session.Watching || _state?.Players.Length is not > 0 || _combat.HasCombat || SpectatorDanmaku.IsEditing || _map.IsPaused) return;
        if (input.IsActionPressed(MegaCrit.Sts2.Core.ControllerInput.MegaInput.viewMap) && !input.IsEcho())
        { _showMap = !_showMap; GetViewport().SetInputAsHandled(); }
    }
    public override void _ExitTree()
    {
        _session.BattleReceived -= Receive; _session.EventPeerReceived -= ReceivePeer;
        if (Instance == this) Instance = null;
    }
}

// 记录普通选择与共享投票，不改写同步器的结果。
// Observe individual choices and shared votes without changing synchronization.
[HarmonyPatch(typeof(EventSynchronizer), "BeginEvent")]
internal static class SpectatorEventBegin
{
    private static void Prefix(EventSynchronizer __instance)
    {
        if (__instance == RunManager.Instance.EventSynchronizer) SpectatorEvents.Instance?.Begin();
    }
}
[HarmonyPatch(typeof(EventSynchronizer), "ChooseOptionForEvent")]
internal static class SpectatorEventChoice
{
    private static void Prefix(EventSynchronizer __instance, Player player, int optionIndex) => SpectatorEvents.Instance?.Chosen(__instance, player, optionIndex, false);
}
[HarmonyPatch(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex")]
internal static class SpectatorEventVote
{
    private static void Prefix(EventSynchronizer __instance, Player player, uint optionIndex, uint pageIndex)
    {
        if (pageIndex == (uint)AccessTools.Field(typeof(EventSynchronizer), "_pageIndex").GetValue(__instance)!)
            SpectatorEvents.Instance?.Chosen(__instance, player, (int)optionIndex, true);
    }
}
