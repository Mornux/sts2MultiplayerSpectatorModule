using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Validation;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 驱动原版只读战斗回放。
// Drive native read-only combat playback.

public partial class SpectatorCombat
{

    // 只在观战端建立原版回放；原菜单保留在树中，关闭后恢复。
    // Start a native replay only on the spectator client and retain the original menu for restoration.
    private async Task StartPlayback(CombatReplay replay, int generation)
    {
        try
        {
            if (RunManager.Instance.IsInProgress) throw new InvalidOperationException(L.T("combat.own_run"));
            _session.ReplayRunning = true;
            var root = NGame.Instance!.RootSceneContainer;
            _previousScene = root.CurrentScene ?? throw new InvalidOperationException(L.T("combat.no_menu"));
            _previousButtons = Descendants(_previousScene).OfType<NButton>().Where(button => button.IsEnabled).ToArray();
            _previousScene.Hide();
            _previousScene.ProcessMode = ProcessModeEnum.Disabled;
            _previousProgress = SaveManager.Instance.Progress;
            SaveManager.Instance.Progress = ProgressState.FromSerializable(_previousProgress.ToSerializable(), new DeserializationContext());
            SaveManager.Instance.Progress.EnableFtues = false;
            _map.SetCombatAvailable(false);
            _map.Show();
            _session.ReportStatus(L.T("combat.loading"));
            _state = RunState.FromSerializable(replay.serializableRun);
            _viewIndex = Math.Max(0, _state.Players.ToList().FindIndex(player => player.NetId == _session.HostId));
            _pendingView = _viewIndex;
            var manager = RunManager.Instance;
            manager.SetUpReplay(_state, replay, _state.Players[_viewIndex].NetId);
            SpectatorMap.Set(manager, "ShouldSave", false);
            manager.CombatReplayWriter.IsEnabled = false;
            manager.CombatStateSynchronizer.IsDisabled = true;
            await PreloadManager.LoadRunAssets(_state.Players.Select(player => player.Character));
            if (generation != _generation) return;
            await PreloadManager.LoadActAssets(_state.Act);
            if (generation != _generation) return;
            manager.Launch();
            _runNode = NRun.Create(_state);
            SpectatorMap.Set(root, "_currentScene", _runNode);
            root.AddChild(_runNode);
            await manager.GenerateMap();
            if (generation != _generation) return;
            manager.ActionQueueSet.FastForwardNextActionId(replay.nextActionId);
            manager.ActionQueueSynchronizer.FastForwardHookId(replay.nextHookId);
            manager.ChecksumTracker.LoadReplayChecksums(null!, replay.nextChecksumId);
            manager.PlayerChoiceSynchronizer.FastForwardChoiceIds(replay.choiceIds);
            manager.RewardsSetSynchronizer.FastForwardRewardIds(replay.rewardIds);
            await manager.LoadIntoLatestMapCoord(AbstractRoom.FromSerializable(replay.serializableRun.PreFinishedRoom, _state));
            if (generation != _generation) return;
            _started = true;
            _loading = false;
            ConfigureCombatUi();
            _map.SetCombatAvailable(!_liveCombatEnded);
            if (!_liveCombatEnded) _map.Hide();
        }
        catch (Exception error) { Fail(L.T("combat.load_failed") + error.Message); }
    }

    public override void _Process(double delta)
    {
        _assembler.Expire(Time.GetTicksMsec());
        if (_session == null) return;
        if (_session.IsHost && !_session.HasViewer) { _lastInk = ""; _inkViewer = 0; }
        if (_session.IsHost && _session.HasViewer && (_inkTick += delta) >= 0.3)
        {
            _inkTick = 0;
            HostInk();
        }
        if (!_session.ReplayRunning || _closing) return;
        _waitTime += delta;
        if (_loading)
        {
            if (_waitTime > 45) Fail(L.T("combat.load_timeout"));
            return;
        }
        if (!_started) return;
        if (_dispatching)
        { if (_waitTime > 30) Fail(L.T("combat.action_timeout")); return; }
        var executor = RunManager.Instance.ActionExecutor;
        if (_pendingView != _viewIndex && !executor.IsRunning && !executor.IsPaused
            && !(NCapstoneContainerInUse())) SwitchView();
        if (_events.TryGetValue(_nextSequence, out var pending))
        {
            long due = pending.Tick - _remoteTick;
            if ((long)(Time.GetTicksMsec() - _localTick) < due) return;
            _events.Remove(_nextSequence++);
            _waitTime = 0;
            TaskHelper.RunSafely(Dispatch(pending.Event, _generation));
        }
        else if (_endSequence >= 0 && _nextSequence > _endSequence && !executor.IsRunning)
            QueueClose();
        else if ((_events.Count > 0 || _endSequence >= _nextSequence) && _waitTime > 10)
            Fail(L.T("combat.missing_events"));
    }

    private static bool NCapstoneContainerInUse() => MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance?.InUse == true;

    // 复用原版回放分发；界面隐藏或打开地图时也继续执行。
    // Use native replay dispatch even while the battle UI is covered by the map.
    private async Task Dispatch(CombatReplayEvent ev, int generation)
    {
        _dispatching = true;
        try
        {
            var manager = RunManager.Instance;
            switch (ev.eventType)
            {
                case CombatReplayEventType.GameAction:
                    while (CombatManager.Instance.EndingPlayerTurnPhaseOne || CombatManager.Instance.EndingPlayerTurnPhaseTwo || manager.ActionExecutor.IsPaused)
                    {
                        if (!await NextFrame(generation)) return;
                    }
                    var action = ev.action!.ToGameAction(_state!.GetPlayer(ev.playerId!.Value)!);
                    if (action.ActionType == GameActionType.CombatPlayPhaseOnly)
                        while (CombatManager.Instance.DebugOnlyGetState()?.CurrentSide == CombatSide.Enemy)
                            if (!await NextFrame(generation)) return;
                    manager.ActionQueueSet.EnqueueWithoutSynchronizing(action);
                    if (action is EndPlayerTurnAction or ReadyToBeginEnemyTurnAction)
                    {
                        var completed = manager.ActionExecutor.FinishedExecutingActions();
                        while (!completed.IsCompleted) if (!await NextFrame(generation)) return;
                        await completed;
                    }
                    break;
                case CombatReplayEventType.HookAction:
                    manager.ActionQueueSet.EnqueueWithoutSynchronizing(manager.ActionQueueSynchronizer.GetHookActionForId(
                        ev.hookId!.Value, ev.playerId!.Value, ev.gameActionType!.Value));
                    break;
                case CombatReplayEventType.ResumeAction:
                    manager.ActionQueueSet.ResumeActionWithoutSynchronizing(ev.actionId!.Value);
                    break;
                case CombatReplayEventType.PlayerChoice:
                    manager.PlayerChoiceSynchronizer.ReceiveReplayChoice(_state!.GetPlayer(ev.playerId!.Value)!, ev.choiceId!.Value, ev.playerChoiceResult!.Value);
                    break;
            }
        }
        catch (Exception error) { Fail(L.T("combat.sync_failed") + error.Message); }
        finally { _dispatching = false; }
    }

    private async Task<bool> NextFrame(int generation)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        return generation == _generation && !_closing && _session.Watching;
    }

    internal bool CatchingUp => _events.Count > 8 || (_events.Count > 0
        && (long)(Time.GetTicksMsec() - _localTick) - (_events.First().Value.Tick - _remoteTick) > 750);
}
