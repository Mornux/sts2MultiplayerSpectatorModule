using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// Read the real synchronizer's existing options. Generating or selecting options can mutate a run.
public partial class SpectatorRestSites : Node
{
    private SpectatorSession _session = null!;
    private SpectatorMap _map = null!;
    private SpectatorCombat _combat = null!;
    private SpectatorRestSiteView _view = null!;
    private readonly BattleAssembler _assembler = new();
    private RestSiteRoom? _room;
    private RestSiteState? _received;
    private string _key = "", _last = "", _receivedKey = "";
    private long _sequence, _receivedSequence = -1;
    private ulong _viewers;
    private double _time, _tick, _sent;
    private bool? _mapOverride;
    private string? _warning;

    public void Initialize(SpectatorSession session, SpectatorMap map, SpectatorCombat combat)
    {
        _session = session; _map = map; _combat = combat;
        _view = new SpectatorRestSiteView();
        map.AddChild(_view); _view.Hide();
        session.BattleReceived += Receive;
    }

    public override void _Process(double delta)
    {
        if (_session == null) return;
        _time += delta;
        _assembler.Expire(Time.GetTicksMsec());
        if (_received is { } old && (!_session.Watching || _session.Map is not { } current
            || old.Map != current.Key || old.Floor != current.Floor || current.Room != nameof(RoomType.RestSite)))
        {
            _received = null; _receivedKey = ""; _receivedSequence = -1; _mapOverride = null;
            _view.Clear();
        }
        bool available = _session.Watching && !_combat.HasCombat && _received is { } state
            && _session.Map is { } map && state.Map == map.Key && state.Floor == map.Floor
            && map.Room == nameof(RoomType.RestSite);
        bool visible = available && !(_mapOverride ?? _received!.MapOpen);
        _map.UpdateRoomContent(_view, visible, 0, available);
        if ((_tick += delta) < 0.25) return;
        _tick = 0;
        if (!_session.IsHost || !_session.HasViewer || _session.ReplayRunning) return;
        try { CaptureAndSend(); }
        catch (Exception error) { Warn("capture", error); }
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
    internal static RestSiteOptionState CaptureOption(RestSiteOption option) => new(
        Limit(option.OptionId, 128), Limit(option.Title.GetFormattedText(), 1024),
        Limit(option.Description.GetFormattedText(), 4000), Limit(option.Icon.ResourcePath, 512), option.IsEnabled);

    private void CaptureAndSend()
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (run?.BaseRoom is not RestSiteRoom room) { _room = null; _last = ""; return; }
        if (!ReferenceEquals(_room, room))
        { _room = room; _key = Guid.NewGuid().ToString("N"); _sequence = 0; _last = ""; _warning = null; }
        var sync = RunManager.Instance.RestSiteSynchronizer;
        var players = new List<RestSitePlayerState>();
        foreach (var player in run.Players.Take(4))
        {
            try
            {
                var options = new List<RestSiteOptionState>();
                foreach (var option in sync.GetOptionsForPlayer(player).Take(32))
                {
                    try { options.Add(CaptureOption(option)); }
                    catch (Exception error) { Warn("option", error); }
                }
                var history = run.CurrentMapPointHistoryEntry?.GetEntry(player.NetId).RestSiteChoices;
                var choices = history?.TakeLast(32).Select(id =>
                {
                    var text = new MegaCrit.Sts2.Core.Localization.LocString("rest_site_ui", "OPTION_" + id + ".name");
                    return Limit(text.Exists() ? text.GetFormattedText() : id, 1024);
                }).ToArray() ?? [];
                players.Add(new(player.NetId, Limit(_session.PlayerName(player.NetId), 40), player.Character.Id.ToString(), options.ToArray(), choices));
            }
            catch (Exception error) { Warn("player", error); }
        }
        var state = new RestSiteState(_key, _session.CurrentMapKey, run.TotalFloor, run.CurrentActIndex, run.Act.Id.ToString(),
            RunManager.Instance.NetService.NetId, players.ToArray(),
            MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance is { IsOpen: true, IsTravelEnabled: true });
        if (!RestSiteBounds.Valid(state)) return;
        string json = JsonSerializer.Serialize(state);
        if (_viewers == _session.ViewerRevision && json == _last && _time - _sent < 1) return;
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > 256000) return;
        _session.SendFragments("rest-view", _key, _sequence, bytes);
        _last = json; _viewers = _session.ViewerRevision; _sent = _time; _sequence++;
    }

    private void Receive(string json)
    {
        if (!_session.Watching) return;
        try
        {
            var frame = JsonSerializer.Deserialize<BattleFrame>(json);
            if (frame is not { Kind: "rest-view", Parts: <= 22 }) return;
            var bytes = _assembler.Accept(frame, Time.GetTicksMsec());
            if (bytes == null || bytes.Length > 256000) return;
            var state = JsonSerializer.Deserialize<RestSiteState>(bytes);
            if (!RestSiteBounds.Valid(state) || state!.Key != frame.Combat || state.Map != _session.Map?.Key
                || state.Floor != _session.Map.Floor || _receivedKey == state.Key && frame.Sequence <= _receivedSequence) return;
            _view.Render(state);
            if (_receivedKey != state.Key) _mapOverride = null;
            _received = state; _receivedKey = state.Key; _receivedSequence = frame.Sequence;
        }
        catch (Exception error) { Warn("receive", error); }
    }

    private void Warn(string operation, Exception error)
    {
        string detail = operation + ": " + error.Message;
        if (_warning == detail) return;
        _warning = detail;
        GD.PushWarning("[Spectator] Rest site " + detail);
    }

    public override void _Input(InputEvent input)
    {
        if (_session == null || !_session.Watching || _received is not { } state || _session.Map is not { } map
            || state.Map != map.Key || state.Floor != map.Floor || map.Room != nameof(RoomType.RestSite)
            || _combat.HasCombat || SpectatorComment.IsEditing || _map.IsPaused || !_map.OwnsRoomInput(_view)) return;
        if (input.IsActionPressed(MegaCrit.Sts2.Core.ControllerInput.MegaInput.viewMap) && !input.IsEcho())
        { _mapOverride = !(_mapOverride ?? state.MapOpen); GetViewport().SetInputAsHandled(); }
    }

    public override void _ExitTree()
    {
        if (_session == null) return;
        _session.BattleReceived -= Receive;
        if (GodotObject.IsInstanceValid(_view))
        {
            if (GodotObject.IsInstanceValid(_map)) _map.UpdateRoomContent(_view, false);
            _view.QueueFree();
        }
    }
}
