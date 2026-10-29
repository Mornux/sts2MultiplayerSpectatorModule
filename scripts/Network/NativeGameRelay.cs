using System.Text;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace sts2MultiplayerSpectatorModule.Scripts;

// Own handler registration and peer validation, without owning the game's connection.
internal sealed class NativeGameRelay(Action<ulong, byte[]> receive) : IDisposable
{
    private INetGameService? _service;

    internal void Update(INetGameService? service)
    {
        if (ReferenceEquals(service, _service)) return;
        _service?.UnregisterMessageHandler<SpectatorLocalRelay>(OnMessage);
        _service = service;
        _service?.RegisterMessageHandler<SpectatorLocalRelay>(OnMessage);
    }

    private void OnMessage(SpectatorLocalRelay message, ulong sender)
    {
        if (_service is NetClientGameService client && client.IsConnected && sender == client.HostNetId
            || _service is NetHostGameService host && host.ConnectedPeers.Any(peer => peer.peerId == sender && peer.readyForBroadcasting))
            receive(sender, Encoding.UTF8.GetBytes(message.Payload));
    }

    public void Dispose() => Update(null);
}
