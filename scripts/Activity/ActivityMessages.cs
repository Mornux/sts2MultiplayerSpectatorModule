using MegaCrit.Sts2.Core.Runs;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 提示只由房主发送，观众校验会话和递增序号，避免重复或伪造。
// Only the host sends notices; viewers validate the session and monotonic sequence.
public partial class SpectatorSession
{
    private readonly ActivityMessageStream _activity = new();
    internal event Action<ActivityNotice>? ActivityReceived
    {
        add => _activity.Received += value;
        remove => _activity.Received -= value;
    }
    internal string MessageLogContext => Watching ? "watch:" + _token : CanSendDanmaku
        ? "run:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(RunManager.Instance.DebugOnlyGetState()!) : "";
    internal static bool ValidActivity(ActivityNotice? notice) => ActivityMessageStream.IsValid(notice);

    internal void PublishActivity(ulong playerId, string action, string text, string color)
    {
        if (!IsHost || !HasViewer || ReplayRunning) return;
        var notice = new ActivityNotice(CleanName(PlayerName(playerId)), action, text, color);
        if (_activity.Create("", notice) is { } packet) BroadcastViewers(packet);
    }

    private bool HandleActivity(ulong sender, SpectatorPacket packet)
    {
        if (packet.Kind != "activity") return false;
        if (Watching && sender == _host && packet.Token == _token) _activity.Receive(packet);
        return true;
    }
}
