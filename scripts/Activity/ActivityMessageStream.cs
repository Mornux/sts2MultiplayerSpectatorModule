using System.Text.Json;

namespace sts2MultiplayerSpectatorModule.Scripts;

internal sealed record ActivityNotice(string Name, string Action, string Text, string Color);

// Own notice validation and ordering. The session authenticates peers before delivery.
// No Godot or game state is needed to exercise this component.
internal sealed class ActivityMessageStream
{
    private long _sequence, _lastReceivedSequence;
    private string _receivedToken = "";
    internal event Action<ActivityNotice>? Received;

    internal static bool IsValid(ActivityNotice? notice) => notice is
        { Name.Length: > 0 and <= 80, Text.Length: > 0 and <= 500, Action: "choice" or "obtain" or "选择了" or "获得了" }
        && !notice.Name.Any(char.IsControl) && !notice.Text.Any(char.IsControl)
        && SpectatorWireProtocol.ValidColor(notice.Color);

    internal SpectatorPacket? Create(string token, ActivityNotice notice) => !IsValid(notice) ? null
        : new(SpectatorWireProtocol.Version, "activity", token,
            JsonSerializer.Serialize(notice, SpectatorWireProtocol.Json), ++_sequence);

    internal void Receive(SpectatorPacket packet)
    {
        if (packet.Kind != "activity" || packet.Sequence < 1) return;
        if (_receivedToken != packet.Token)
        {
            _receivedToken = packet.Token;
            _lastReceivedSequence = 0;
        }
        if (packet.Sequence <= _lastReceivedSequence || packet.Text.Length > 4000) return;
        try
        {
            var notice = JsonSerializer.Deserialize<ActivityNotice>(packet.Text);
            if (!IsValid(notice)) return;
            _lastReceivedSequence = packet.Sequence;
            Received?.Invoke(notice!);
        }
        catch (JsonException) { }
    }
}
