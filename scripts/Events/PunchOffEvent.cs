namespace sts2MultiplayerSpectatorModule.Scripts.Events;

// 斗殴事件显示选项和共享投票，不运行背景互殴动画。
// Show Punch-Off options and shared votes without running background combat actors.
internal static class PunchOffEvent
{
    internal static EventPlayerState Enrich(EventPlayerState state) => state with
    { Details = L.Wire("events.punch_off") };
}
