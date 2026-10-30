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
        if (HandleViewerData(sender, packet)) return;
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
            else if (_viewers.TryGetValue(sender, out var active))
            {
                if (active.Token != packet.Token) { Send(sender, new(2, "end", packet.Token, L.Wire("status.rejected"))); return; }
                active.Seen = _time; UpdateViewerColor(sender, packet.Color);
            }
            else if (_viewers.Count >= MaxViewers)
                Send(sender, new(2, "end", packet.Token, L.Wire("status.full")));
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
                _viewers.Add(sender, new ViewerConnection(packet.Token, CleanName(SpectatorLocal.IsLocal(sender) ? packet.Name : PlayerName(sender)),
                    ValidColor(packet.Color) ? packet.Color.ToLowerInvariant() : "dca747", _time));
                Send(sender, new(2, "map", packet.Token, snapshot, ++_sequence));
                PublishRoster();
                foreach (var line in _ink) Send(sender, new(2, "viewer-ink", packet.Token, JsonSerializer.Serialize(line)));

            }
        }
        else if (packet.Kind == "leave" && _requests.TryGetValue(sender, out var pending) && pending.Token == packet.Token)
            _requests.Remove(sender);
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
