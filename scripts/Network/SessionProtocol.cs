using System.Text.Json;
using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 验证身份并分发会话消息。
// Authenticate peers and dispatch session messages.

public partial class SpectatorSession
{

    // 房主只接受观战请求和退出；观众只接受所选房主的数据。
    // Hosts accept watch and leave requests; viewers accept only their selected host.
    private void Handle(ulong sender, SpectatorPacket packet)
    {
        if (HandleEventPeer(sender, packet)) return;
        if (HandleDanmaku(sender, packet)) return;
        if (HandleActivity(sender, packet)) return;
        if (packet.Kind == "query")
        {
            Send(sender, new(2, "offer", packet.Token, !IsHost ? L.Wire("status.not_host")
                : IsLocalHost ? L.Wire("status.use_local") : !AllowSpectators ? L.Wire("status.disabled")
                : CanEnter() ? L.Wire("status.available") : L.Wire("status.wait_map")));
        }
        else if (packet.Kind == "offer" && packet.Token == _discovery && _friends.ContainsKey(sender))
        {
            _friends[sender] = L.Resolve(packet.Text);
            FriendsChanged?.Invoke();
        }
        else if (packet.Kind == "watch")
        {
            if (!IsHost || !CanContactViewer(sender))
                Send(sender, new(2, "end", packet.Token, L.Wire("status.rejected")));
            else if (_viewer != 0 && (_viewer != sender || _viewerToken != packet.Token))
                Send(sender, new(2, "end", packet.Token, L.Wire("status.full")));
            else if (_viewer == sender && _viewerToken == packet.Token)
            { _viewerSeen = _time; UpdateViewerColor(packet.Color); }
            else if (AwaitApproval(sender, packet)) return;
            else if (!CanEnter())
                Send(sender, new(2, "wait", packet.Token, L.Wire("status.wait_ready")));
            else
            {
                // 快照生成成功后才接纳观众，避免异常留下一个无法使用的观众名额。
                // Admit only after creating a snapshot, so failures cannot leave a stuck viewer slot.
                string? snapshot = Snapshot();
                if (snapshot == null)
                { Send(sender, new(2, "wait", packet.Token, L.Wire("status.wait_init"))); return; }
                _run = RunManager.Instance.DebugOnlyGetState();
                _requests.Remove(sender);
                _viewer = sender;
                _viewerName = SpectatorLocal.IsLocal(sender) ? CleanName(packet.Name) : PlayerName(sender);
                _viewerToken = packet.Token;
                _viewerSeen = _time;
                _sequence = 0;
                _viewerColor = ValidColor(packet.Color) ? packet.Color.ToLowerInvariant() : "dca747";
                RelayInk("roster", _viewer.ToString());
                Send(sender, new(2, "map", packet.Token, snapshot, ++_sequence));
            }
        }
        else if (IsHost && sender == _viewer && packet.Token == _viewerToken && packet.Kind == "color")
            UpdateViewerColor(packet.Color);
        else if (IsHost && sender == _viewer && packet.Token == _viewerToken && packet.Kind is "draw" or "erase")
        {
            var sketch = JsonSerializer.Deserialize<Sketch>(packet.Text);
            if (!ValidSketch(sketch, _mapKey, _mapAct) || sketch!.Live && _time - _lastSketch < 0.035) return;
            _lastSketch = _time;
            if (!ApplySketch(packet.Kind == "erase" ? sketch! with { Eraser = true } : sketch!)) return;
            SyncNativeInk();
            RelayInk(packet.Kind == "erase" ? "erase-ink" : "ink", packet.Text);
        }
        else if (IsHost && sender == _viewer && packet.Token == _viewerToken && packet.Kind == "clear")
        {
            ResetInk();
            RelayInk("clear-ink", "");
        }
        else if (FromGameHost(sender) && packet.Kind == "roster")
        {
            if (ulong.TryParse(packet.Text, out ulong viewer))
            {
                _remoteViewer = viewer; _remoteViewerName = CleanName(packet.Name);
                if (ValidColor(packet.Color)) _remoteViewerColor = packet.Color.ToLowerInvariant();
                SyncNativeInk();
            }
        }
        else if (FromGameHost(sender) && packet.Kind is "ink" or "erase-ink" or "clear-ink" or "color")
        {
            if (ValidColor(packet.Color)) _remoteViewerColor = packet.Color.ToLowerInvariant();
            if (packet.Kind == "color") { SyncNativeInk(); return; }
            int act = RunManager.Instance.DebugOnlyGetState()!.CurrentActIndex;
            if (packet.Kind == "clear-ink")
            {
                if (!int.TryParse(packet.Text, out int clearAct) || clearAct != act) return;
                ResetInk();
            }
            else
            {
                var sketch = JsonSerializer.Deserialize<Sketch>(packet.Text);
                if (!ValidSketch(sketch, packet.Token, act)) return;
                if (_mapKey != packet.Token) { ResetInk(); _mapKey = packet.Token; }
                if (!ApplySketch(packet.Kind == "erase-ink" ? sketch! with { Eraser = true } : sketch!)) return;
            }
            SyncNativeInk();
        }
        else if (packet.Kind == "leave" && _requests.TryGetValue(sender, out var pending) && pending.Token == packet.Token)
            _requests.Remove(sender);
        else if (packet.Kind == "leave" && sender == _viewer && packet.Token == _viewerToken)
        {
            _viewer = 0;
            RelayInk("roster", "0");
            if (SpectatorLocal.IsLocal(sender)) _local?.ClosePeer(sender);
        }
        else if (sender == _host && packet.Token == _token)
        {
            if (packet.Kind == "battle") { _received = _time; BattleReceived?.Invoke(packet.Text); return; }
            if (packet.Kind == "end") { StopWatching(packet.Text); return; }
            if (packet.Kind == "wait") { _received = _time; SetStatus(packet.Text); }
            if (packet.Kind == "map" && packet.Sequence > _lastSequence)
            {
                var map = JsonSerializer.Deserialize<MapSnapshot>(packet.Text);
                if (!ValidMap(map)) return;
                if (Map?.Key != map!.Key) ResetInk();
                Map = map;
                _received = _time;
                _lastSequence = packet.Sequence;
                // 房间枚举来自网络，显示时按观众语言解析，不直接输出英文标识。
                // Translate network room identifiers in the viewer's language instead of displaying raw enums.
                string room = Enum.TryParse<MegaCrit.Sts2.Core.Rooms.RoomType>(map.Room, out var type)
                    && Enum.IsDefined(type) ? type.ToString() : "Unassigned";
                SetStatus(L.T("status.watching", L.T("room." + room)));
            }
        }
    }
}
