using System.Net;
using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Platform;

namespace sts2MultiplayerSpectatorModule.Scripts;

// 复用原版 ENet 传输，单独监听游戏端口加一，不进入四人大厅。
// Reuse native ENet on the game port plus one without entering the four-player lobby.
internal sealed class SpectatorLocal : INetHostHandler, INetClientHandler, IDisposable
{
    internal const ulong Host = ulong.MaxValue;
    private const ulong Prefix = 0xffff000000000000;
    internal static ushort GamePort = 33771;
    private readonly ulong _identity = Prefix | (BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)) & 0x0000ffffffffffff);
    private readonly Action<ulong, byte[]> _receive;
    private readonly Action<string> _failed;
    private ENetHost? _server;
    private ENetClient? _client;
    private CancellationTokenSource? _cancel;
    private bool _listening, _closing, _updating, _disconnectPending;
    private int _generation;
    internal string Error { get; private set; } = "";
    internal static bool IsLocal(ulong id) => (id & Prefix) == Prefix;
    internal bool IsPeer(ulong id) => _server?.ConnectedPeerIds.Contains(id) == true;

    internal SpectatorLocal(Action<ulong, byte[]> receive, Action<string> failed)
    { _receive = receive; _failed = failed; }

    internal void Update(bool listen)
    {
        if (listen != _listening)
        {
            _listening = listen;
            Error = "";
            if (listen)
            {
                if (GamePort == ushort.MaxValue) Error = L.T("local.invalid_port");
                else
                {
                    var server = new ENetHost(this);
                    var error = server.StartHost((ushort)(GamePort + 1), 4);
                    if (error.HasValue)
                    {
                        Error = L.T("local.listen_failed", GamePort + 1);
                        ((ENetConnection?)AccessTools.Field(typeof(ENetHost), "_connection").GetValue(server))?.Dispose();
                    }
                    else _server = server;
                }
                if (Error != "") GD.PushWarning(Error);
            }
            else
            {
                PruneDisconnected();
                var old = _server;
                _server = null;
                old?.StopHost(NetError.Quit, true);
                if (old != null)
                    ((ENetConnection?)AccessTools.Field(typeof(ENetHost), "_connection").GetValue(old))?.Dispose();
            }
        }
        _updating = true;
        try
        {
            PruneDisconnected();
            _server?.Update();
            if (_client?.IsConnected == true) _client.Update();
        }
        finally
        {
            _updating = false;
            if (_disconnectPending) { _disconnectPending = false; Disconnect(); }
        }
    }

    internal void ClosePeer(ulong id)
    {
        PruneDisconnected();
        if (IsPeer(id)) _server!.DisconnectClient(id, NetError.Quit);
    }

    // 原版 ENetHost 忽略底层断开事件，观战侧主动移除已断开的缓存连接。
    // Native ENetHost ignores transport disconnect events; prune stale spectator connections.
    private void PruneDisconnected()
    {
        if (_server == null) return;
        var peers = (IList)AccessTools.Field(typeof(ENetHost), "_connectedPeers").GetValue(_server)!;
        for (int index = peers.Count - 1; index >= 0; index--)
        {
            object connection = peers[index]!;
            var peer = (ENetPacketPeer)AccessTools.Field(connection.GetType(), "peer").GetValue(connection)!;
            if (peer.IsActive() && peer.GetState() == ENetPacketPeer.PeerState.Connected) continue;
            AccessTools.Method(typeof(ENetHost), "HandleClientDisconnection")
                .Invoke(_server, new[] { connection, (object)NetError.Quit, true });
        }
    }

    // 地址必须明确指定；本机测试使用 127.0.0.1。
    // Require an explicit address; use 127.0.0.1 for same-machine testing.
    internal static bool ValidAddress(string ip, int port) => port is > 0 and < 65535
        && IPAddress.TryParse(ip, out var address)
        && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
        && !IPAddress.Any.Equals(address) && !IPAddress.Broadcast.Equals(address)
        && address.GetAddressBytes()[0] is > 0 and < 224;

    internal async Task Connect(string ip, int port, Action connected)
    {
        Disconnect();
        int generation = _generation;
        var client = _client = new ENetClient(this);
        _cancel = new CancellationTokenSource();
        try
        {
            var error = await client.ConnectToHost(_identity, ip, (ushort)(port + 1), _cancel.Token);
            if (generation != _generation) return;
            if (error.HasValue || !client.IsConnected)
                _failed(L.T("local.connect_failed"));
            else connected();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation == _generation) _failed(L.T("local.error") + exception.Message);
        }
    }

    internal void Send(ulong peer, byte[] bytes)
    {
        PruneDisconnected();
        if (peer == Host)
        {
            if (_client?.IsConnected == true) _client.SendMessageToHost(bytes, bytes.Length, NetTransferMode.Reliable);
        }
        else if (IsPeer(peer)) _server!.SendMessageToClient(peer, bytes, bytes.Length, NetTransferMode.Reliable);
    }

    internal void Disconnect()
    {
        if (_updating) { _disconnectPending = true; return; }
        _generation++;
        _cancel?.Cancel();
        _cancel?.Dispose();
        _cancel = null;
        var old = _client;
        _client = null;
        _closing = true;
        bool established = old?.IsConnected == true;
        var nativePeer = old == null ? null : (ENetPacketPeer?)AccessTools.Field(typeof(ENetClient), "_peer").GetValue(old);
        bool active = nativePeer?.IsActive() == true;
        if (active) old!.DisconnectFromHost(NetError.Quit, true);
        // 原版只会在握手完成后销毁连接，取消握手也要释放本地端口。
        // Native disconnect destroys only established connections; also release cancelled handshakes.
        if (old != null)
        {
            var connection = (ENetConnection?)AccessTools.Field(typeof(ENetClient), "_connection").GetValue(old);
            if (!established || !active) connection?.Destroy();
            connection?.Dispose();
        }
        _closing = false;
    }

    public void OnPacketReceived(ulong senderId, byte[] bytes, NetTransferMode mode, int channel)
    {
        if (bytes.Length is > 0 and <= SpectatorWireProtocol.MaxBytes && channel == 0)
            _receive(_client != null ? Host : senderId, bytes);
    }
    public void OnConnectedToHost() { }
    public void Initialize(NetClient client, PlatformType platform) { }
    public void OnPeerConnected(ulong peerId)
    {
        if (!IsLocal(peerId) || peerId == Host) _server?.DisconnectClient(peerId, NetError.InvalidJoin, true);
    }
    public void OnPeerDisconnected(ulong peerId, NetErrorInfo info) { }
    public void OnDisconnected(NetErrorInfo info) { }
    public void OnDisconnectedFromHost(ulong hostNetId, NetErrorInfo info)
    { if (!_closing) _failed(L.T("local.disconnected")); }
    public void Dispose() { Disconnect(); Update(false); }
}
