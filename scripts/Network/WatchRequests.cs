namespace sts2MultiplayerSpectatorModule.Scripts;

// 申请按连接令牌绑定，心跳续期；批准后仍等待可进入地图，不占玩家名额。
// Bind requests to connection tokens and heartbeat expiry; approved viewers still wait for a safe map.
public partial class SpectatorSession
{
    internal sealed record WatchRequest(string Token, string Name, double Seen, bool Approved = false);
    private readonly Dictionary<ulong, WatchRequest> _requests = new();
    internal IReadOnlyDictionary<ulong, WatchRequest> Requests => _requests;

    internal void ApproveViewer(ulong id)
    {
        if (!IsHost || HasViewer || !_requests.TryGetValue(id, out var request)) return;
        AllowSpectators = true;
        _requests[id] = request with { Approved = true };
    }

    private bool AwaitApproval(ulong sender, SpectatorPacket packet)
    {
        if (!_requests.TryGetValue(sender, out var request) || request.Token != packet.Token)
        {
            if (_requests.Count >= 16) return true;
            request = new(packet.Token, CleanName(SpectatorLocal.IsLocal(sender) ? packet.Name : PlayerName(sender)), _time);
        }
        _requests[sender] = request with { Seen = _time };
        if (request.Approved && AllowSpectators) return false;
        Send(sender, new(2, "wait", packet.Token, L.Wire("status.await_approval")));
        return true;
    }
}
