using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 原版战斗回放作为只读运行时，消息走独立观战通道。
// Run native combat replays read-only over the independent spectator transport.
public partial class SpectatorCombat : Node
{
    public static SpectatorCombat? Instance { get; private set; }
    public static bool Watching => Instance?._session?.ReplayRunning == true;
    private SpectatorSession _session = null!;
    private SpectatorMap _map = null!;
    private readonly SortedDictionary<long, (CombatReplayEvent Event, long Tick)> _events = new();
    private ulong _localTick;
    private int _generation, _viewIndex, _pendingView;
    private bool _loading, _dispatching, _closing, _started, _liveCombatEnded, _closeScheduled;
    private RunState? _state;
    private NRun? _runNode;
    private ReplayEnvironment? _environment;
    private MapStateSync _mapSync = null!;
    private double _waitTime;
    private readonly HashSet<string> _finished = new();
    public bool HasCombat => _session.ReplayRunning && !_closing;

    public void Initialize(SpectatorSession session, SpectatorMap map)
    {
        Instance = this;
        _session = session;
        _map = map;
        _mapSync = new MapStateSync(session, map);
        session.BattleReceived += Receive;
        session.Updated += OnSessionChanged;
        map.ReturnToCombatRequested += HideMap;
        CombatManager.Instance.CombatEnded += OnCombatEnded;
    }

    internal static bool Owns(Node node)
    {
        if (!Watching || Instance?._runNode is not { } run) return false;
        for (Node? current = node; current != null; current = current.GetParent())
            if (current == run) return true;
        return false;
    }

    internal static string? PlayerDisplayName(Node owner, ulong id) => Owns(owner)
        && Instance?._state?.Players.Any(player => player.NetId == id) == true ? Instance._session.PlayerName(id) : null;

    internal static bool OwnsActionQueue(object synchronizer) => Watching
        && Instance?._state is { } state && ReferenceEquals(state, RunManager.Instance.DebugOnlyGetState())
        && ReferenceEquals(synchronizer, RunManager.Instance.ActionQueueSynchronizer);

    private void OnSessionChanged()
    {
        if (!_session.Watching && _session.ReplayRunning && !_closing) QueueClose();
        UpdateTimer();
    }

    internal void EndLocally(object turnState)
    {
        if (!ReferenceEquals(turnState, AccessTools.Field(typeof(CombatManager), "_turnState").GetValue(CombatManager.Instance))) return;
        SpectatorMap.Set(turnState, "IsInProgress", false);
        RunManager.Instance.ActionExecutor.Unpause();
        _liveCombatEnded = true;
        _map.SetCombatAvailable(false);
        if (!_map.Visible) TaskHelper.RunSafely(SwitchAutomatic(_map.Show, _generation));
    }

    private void Fail(string message)
    {
        GD.PushWarning("[Spectator] " + message);
        _session.ReportStatus(message);
        QueueClose();
    }

    private void QueueClose()
    {
        if (_closing || _closeScheduled) return;
        _closeScheduled = true;
        Callable.From(() => TaskHelper.RunSafely(ClosePlayback())).CallDeferred();
    }

    // 先取消回放动作再清理原版节点；不保存、不结算，并恢复原菜单。
    // Cancel replay actions before restoring the original menu without saving or settling rewards.
    private async Task ClosePlayback()
    {
        if (_closing) return;
        _closing = true;
        int generation = _generation;
        bool restored = false;
        try
        {
            await _automaticSwitch;
            if (_session.Watching && _started && !_map.Visible)
                await SwitchAutomatic(_map.Show, _generation);
        }
        catch (Exception error) { GD.PushError("[Spectator] Replay cleanup: " + error); }
        finally
        {
            if (generation == _generation) { RestorePlayback(); restored = true; }
        }
        if (!restored || !IsInsideTree()) return;
        if (_session.Watching) _map.Show(); else _map.Hide();
        var queued = _nextBattlePackets.ToArray();
        _nextBattlePackets.Clear();
        _waitingBattle = "";
        _nextBattleBytes = 0;
        if (_session.Watching) foreach (string packet in queued) Receive(packet);
    }

    // Also usable after leaving the tree: no frame waits and no deferred work.
    private void RestorePlayback()
    {
        ++_generation;
        if (_combat.Length != 0) _finished.Add(_combat);
        if (_finished.Count > 32) _finished.Remove(_finished.First());
        _events.Clear();
        _assembler.Clear();
        _started = _loading = false;
        ReplayEnvironment.Restore("shared UI", SpectatorUiLayers.CloseInspections);
        ReplayEnvironment.Restore("run", () =>
        {
            if (_state != null && ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), _state))
                RunManager.Instance.CleanUp(graceful: false);
        });
        _environment?.Dispose();
        _environment = null;
        ReplayEnvironment.Restore("hover tips", () =>
        {
            if (_runNode != null && GodotObject.IsInstanceValid(_runNode)) ClearOwnedHoverTips(_runNode);
        });
        ReplayEnvironment.Restore("run scene", () =>
        {
            if (_runNode != null && GodotObject.IsInstanceValid(_runNode)) _runNode.Free();
        });
        _runNode = null;
        _state = null;
        _combat = "";
        // Keep read-only guards active through all native cleanup and restoration.
        _session.ReplayRunning = false;
        _closing = _closeScheduled = false;
        ReplayEnvironment.Restore("map", () => _map.SetCombatAvailable(false));
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    public override void _ExitTree()
    {
        if (_session != null)
        {
            _session.BattleReceived -= Receive;
            _session.Updated -= OnSessionChanged;
            _map.ReturnToCombatRequested -= HideMap;
            CombatManager.Instance.CombatEnded -= OnCombatEnded;
            if (_session.ReplayRunning || _environment != null) RestorePlayback();
        }
        if (Instance == this) Instance = null;
    }
}

