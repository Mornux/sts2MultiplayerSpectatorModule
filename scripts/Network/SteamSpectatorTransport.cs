using System.Runtime.InteropServices;
using Godot;
using MegaCrit.Sts2.Core.Platform.Steam;
using Steamworks;

namespace sts2MultiplayerSpectatorModule.Scripts;

// Own only Steam resources. Session policy and packet dispatch are supplied by the owner.
internal sealed class SteamSpectatorTransport(Func<ulong, bool> isPeer, Action<ulong, byte[]> receive) : IDisposable
{
    private const int Channel = 238141;
    private readonly IntPtr[] _messages = new IntPtr[16];
    private Callback<SteamNetworkingMessagesSessionRequest_t>? _requests;

    internal void Update()
    {
        if (!SteamInitializer.Initialized) return;
        _requests ??= Callback<SteamNetworkingMessagesSessionRequest_t>.Create(request =>
        {
            if (isPeer(request.m_identityRemote.GetSteamID64()))
                SteamNetworkingMessages.AcceptSessionWithUser(ref request.m_identityRemote);
        });
        int count = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, _messages, _messages.Length);
        for (int i = 0; i < count; i++)
        {
            try
            {
                var message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(_messages[i]);
                ulong sender = message.m_identityPeer.GetSteamID64();
                if (!isPeer(sender) || message.m_cbSize is <= 0 or > SpectatorWireProtocol.MaxBytes) continue;
                var bytes = new byte[message.m_cbSize];
                Marshal.Copy(message.m_pData, bytes, 0, bytes.Length);
                receive(sender, bytes);
            }
            catch (Exception exception) { GD.PushWarning($"Spectator packet ignored: {exception.Message}"); }
            finally { SteamNetworkingMessage_t.Release(_messages[i]); }
        }
    }

    internal void Send(ulong peer, byte[] bytes)
    {
        if (!SteamInitializer.Initialized || bytes.Length is < 1 or > SpectatorWireProtocol.MaxBytes) return;
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID64(peer);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            SteamNetworkingMessages.SendMessageToUser(ref identity, handle.AddrOfPinnedObject(),
                (uint)bytes.Length, Constants.k_nSteamNetworkingSend_Reliable
                    | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession, Channel);
        }
        finally { handle.Free(); }
    }

    // Other mods may share these Steam sessions; release only our callback.
    public void Dispose()
    {
        _requests?.Dispose();
        _requests = null;
    }
}
