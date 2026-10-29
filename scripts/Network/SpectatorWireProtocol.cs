using System.Text.Json;

namespace sts2MultiplayerSpectatorModule.Scripts;

// Wire format shared by Steam, ENet and native game relays; independent of Godot.
internal sealed record SpectatorPacket(int Version, string Kind, string Token, string Text = "",
    long Sequence = 0, string Name = "", string Color = "dca747");

internal static class SpectatorWireProtocol
{
    internal const int Version = 2;
    internal const int MaxBytes = 24000;
    internal static readonly JsonSerializerOptions Json = new()
    { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static byte[] Encode(SpectatorPacket packet) => JsonSerializer.SerializeToUtf8Bytes(packet, Json);

    internal static bool ValidColor(string? color) => color is { Length: 6 }
        && color.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    // Malformed JSON still throws so the caller can preserve transport-specific logging.
    internal static SpectatorPacket? Decode(byte[] bytes)
    {
        if (bytes.Length is < 1 or > MaxBytes) return null;
        var packet = JsonSerializer.Deserialize<SpectatorPacket>(bytes);
        return packet is { Version: Version, Token.Length: 32, Text: not null, Name.Length: <= 80 }
            ? packet : null;
    }
}
