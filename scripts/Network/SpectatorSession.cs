using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 独立消息通道，不进入原版大厅或玩家列表。
// Use a separate message channel without joining the game lobby or player list.
public partial class SpectatorSession : Node
{
    internal static readonly JsonSerializerOptions WireJson = SpectatorWireProtocol.Json;
    private readonly Dictionary<ulong, string> _friends = new();
    private RunState? _run;
    private ulong _host;
    private string _token = "", _discovery = "";
    private double _time, _tick, _heartbeat, _received;
    private long _sequence, _lastSequence;
    private string _mapKey = Guid.NewGuid().ToString("N");
    private int _mapAct = -1;
    private RunState? _inkRun;
    private ulong _remoteViewer;
    private double _lastRoster;
    private SpectatorLocal? _local;
    private string _remoteViewerName = "", _localName = L.T("common.spectator");

    private SpectatorLocal Local => _local ??= new SpectatorLocal(ReceivePacket, reason => StopWatching(reason));
    public bool IsLocalHost => IsHost && RunManager.Instance.NetService.Platform == PlatformType.None;
    public string LocalHostStatus => _local?.Error is { Length: > 0 } error ? error
        : L.T("local.port", SpectatorLocal.SpectatorPort);

    public bool AllowSpectators { get; set; }
    public bool IsHost => RunManager.Instance.IsInProgress
        && !RunManager.Instance.IsCleaningUp && RunManager.Instance.NetService.Type == NetGameType.Host;
    public bool Watching => _host != 0;
    public bool HasViewer => _viewers.Count > 0;
    public ulong ViewerId => _viewers.Keys.FirstOrDefault();
    public ulong HostId => _host == SpectatorLocal.Host ? 1UL : _host;
    public PlatformType WatchPlatform => _host == SpectatorLocal.Host ? PlatformType.None : PlatformType.Steam;
    public ulong DisplayViewerId => IsHost ? ViewerId : _remoteViewer;
    public MapSnapshot? Map { get; private set; }
    public string CurrentMapKey => Map?.Key ?? _mapKey;
    public IReadOnlyList<Sketch> Ink => _ink;
    public string Status { get; private set; } = L.T("status.choose_host");
    public IReadOnlyDictionary<ulong, string> Friends => _friends;
    public event Action? FriendsChanged;
    public event Action? Updated;
    public bool ReplayRunning { get; set; }
    public event Action<string>? BattleReceived;
    public void ReportStatus(string status) => SetStatus(status);

    // 战斗消息沿用已认证的观战会话，立即发送而不等待地图轮询。
    // Send battle data immediately through the authenticated spectator session.
    public void SendBattle(string data)
    {
        if (IsHost) BroadcastViewers(new(2, "battle", "", data));
    }

    public override void _Process(double delta)
    {
        _time += delta;
        var localRun = !ReplayRunning && RunManager.Instance.IsInProgress ? RunManager.Instance.DebugOnlyGetState() : null;
        if (!ReferenceEquals(_inkRun, localRun))
        {
            _inkRun = localRun;
            _remoteViewer = 0;
            if (!Watching) _remoteViewers.Clear();
            ResetInk();
        }
        Steam.Update();
        UpdateRelayService();
        if (IsLocalHost || _local != null) Local.Update(IsLocalHost);
        if ((_tick += delta) < 0.5) return;
        _tick = 0;
        foreach (var id in _requests.Where(pair => _time - pair.Value.Seen > 12).Select(pair => pair.Key).ToArray()) _requests.Remove(id);
        if (!ReplayRunning && RunManager.Instance.IsInProgress && _ink.Count > 0) SyncNativeInk();

        var run = IsHost ? RunManager.Instance.DebugOnlyGetState() : null;
        if (!ReferenceEquals(_run, run))
        {
            RemoveViewer(L.Wire("status.run_changed"));
            _requests.Clear();
            _mapAct = -1;
            _run = run;
            _danmakuTimes.Clear();
        }
        foreach (var pair in _viewers.ToArray())
            if (!AllowSpectators || !CanContactViewer(pair.Key) || _time - pair.Value.Seen > 12)
                RemoveViewer(pair.Key, L.Wire("status.closed"));
        if (HasViewer)
        {
            var snapshot = Snapshot();
            BroadcastViewers(snapshot != null ? new(2, "map", "", snapshot, ++_sequence)
                : new(2, "wait", "", L.Wire("status.wait_init")));
        }
        if (!ReplayRunning) SyncNativeInk();
        if (IsHost && _time - _lastRoster >= 2)
        {
            _lastRoster = _time;
            PublishRoster();
        }
        if (_host == 0) return;
        if (RunManager.Instance.IsInProgress && !ReplayRunning)
        {
            StopWatching(L.T("status.own_run"));
            return;
        }
        if (_time - _received > 15)
        {
            StopWatching(L.T("status.timeout"));
            return;
        }
        if (_time - _heartbeat >= 3)
        {
            _heartbeat = _time;
            Send(_host, new(2, "watch", _token, Name: _localName, Color: DrawingColor));
        }
    }

    private void SetStatus(string status) { Status = L.Resolve(status); Updated?.Invoke(); }

    // 释放本模组回调，不关闭其他模组可能共用的 Steam 会话。
    // Release our callback without closing Steam sessions shared with other mods.
    public override void _ExitTree()
    {
        StopWatching();
        RemoveViewer(L.Wire("status.host_left"));
        _steam?.Dispose();
        _relay?.Dispose();
        _local?.Dispose();
    }
}
