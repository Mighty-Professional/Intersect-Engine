using System.Collections.Concurrent;
using System.Net;
using Intersect.Core;
using Intersect.Memory;
using Intersect.Network;
using Intersect.Network.Events;
using Intersect.Server.Networking.LiteNetLib;
using Microsoft.Extensions.Logging;

namespace Intersect.Server.Networking.WebSocket;

/// <summary>
/// INetworkLayerInterface implementation for WebSocket transport.
/// Allows web browser clients to connect to the game server alongside LiteNetLib UDP clients.
/// </summary>
public sealed class WebSocketNetworkInterface : INetworkLayerInterface
{
    private INetwork? _network;
    private readonly ConcurrentDictionary<Guid, WebSocketConnection> _connections = new();
    private readonly ConcurrentQueue<(WebSocketConnection Connection, byte[] Data)> _inboundQueue = new();
    private bool _running;

    /// <summary>
    /// Attaches this interface to the server network.
    /// Must be called after the server network is initialized.
    /// </summary>
    public void AttachToNetwork(ServerNetwork serverNetwork)
    {
        _network = serverNetwork;
        serverNetwork.AddTransport(this);
    }

    public event HandleConnectionEvent? OnConnected;
    public event HandleConnectionEvent? OnConnectionApproved;
    public event HandleConnectionEvent? OnConnectionDenied;
    public event HandleConnectionRequest? OnConnectionRequested;
    public event HandleConnectionEvent? OnDisconnected;
    public event HandlePacketAvailable? OnPacketAvailable;
    public event HandleUnconnectedMessage? OnUnconnectedMessage;

    /// <summary>
    /// Called by the ASP.NET WebSocket middleware when a new WebSocket connection is accepted.
    /// </summary>
    public WebSocketConnection? AcceptConnection(System.Net.WebSockets.WebSocket socket, string ip, int port)
    {
        if (_network == null)
        {
            ApplicationContext.Context.Value?.Logger.LogWarning(
                "WebSocket connection rejected: network not attached");
            return null;
        }

        var connection = new WebSocketConnection(socket, this, ip, port);

        // Check if connection is approved
        var approved = OnConnectionRequested?.Invoke(this, connection) ?? true;
        if (!approved)
        {
            OnConnectionDenied?.Invoke(
                this,
                new ConnectionEventArgs { Connection = connection, NetworkStatus = NetworkStatus.Offline }
            );
            connection.Dispose();
            return null!;
        }

        _connections[connection.Guid] = connection;

        // Fire connected events - ServerNetwork.HandleInterfaceOnConnected will call
        // Client.CreateBeta4Client which handles adding to the network's connection list
        var eventArgs = new ConnectionEventArgs
        {
            Connection = connection,
            NetworkStatus = NetworkStatus.Online,
        };

        OnConnected?.Invoke(this, eventArgs);
        connection.HandleConnected();
        OnConnectionApproved?.Invoke(this, eventArgs);

        ApplicationContext.Context.Value?.Logger.LogInformation(
            $"WebSocket client connected [{connection.Guid}] from {ip}:{port}");

        return connection;
    }

    /// <summary>
    /// Called by WebSocketConnection when data is received.
    /// </summary>
    internal void EnqueueInboundData(WebSocketConnection connection, byte[] data)
    {
        _inboundQueue.Enqueue((connection, data));
        OnPacketAvailable?.Invoke(this);
    }

    /// <summary>
    /// Called by WebSocketConnection when the connection is lost.
    /// </summary>
    internal void HandleDisconnection(WebSocketConnection connection)
    {
        if (!_connections.TryRemove(connection.Guid, out _)) return;

        // ServerNetwork.HandleInterfaceOnDisconnected will call Client.RemoveBeta4Client
        OnDisconnected?.Invoke(
            this,
            new ConnectionEventArgs
            {
                Connection = connection,
                NetworkStatus = NetworkStatus.Offline,
            }
        );

        ApplicationContext.Context.Value?.Logger.LogInformation(
            $"WebSocket client disconnected [{connection.Guid}]");
    }

    public bool TryGetInboundBuffer(out IBuffer buffer, out IConnection connection)
    {
        if (_inboundQueue.TryDequeue(out var item))
        {
            connection = item.Connection;
            buffer = new MemoryBuffer(item.Data);
            return true;
        }

        buffer = default!;
        connection = default!;
        return false;
    }

    public void ReleaseInboundBuffer(IBuffer buffer)
    {
        // MemoryBuffer doesn't need special release handling
    }

    public bool SendPacket(
        IPacket packet,
        IConnection? connection = null,
        TransmissionMode transmissionMode = TransmissionMode.All)
    {
        if (connection is WebSocketConnection wsConnection)
        {
            return wsConnection.Send(packet, transmissionMode);
        }

        // If no specific connection, try first WebSocket connection
        var firstConnection = _connections.Values.FirstOrDefault();
        return firstConnection?.Send(packet, transmissionMode) ?? false;
    }

    public bool SendPacket(
        IPacket packet,
        ICollection<IConnection> connections,
        TransmissionMode transmissionMode = TransmissionMode.All)
    {
        var success = true;
        foreach (var connection in connections)
        {
            if (connection is WebSocketConnection wsConnection)
            {
                if (!wsConnection.Send(packet, transmissionMode))
                    success = false;
            }
        }
        return success;
    }

    public bool SendUnconnectedPacket(IPEndPoint target, ReadOnlySpan<byte> data) => false;
    public bool SendUnconnectedPacket(IPEndPoint target, UnconnectedPacket packet) => false;

    public void Start()
    {
        _running = true;
        ApplicationContext.Context.Value?.Logger.LogInformation("WebSocket network interface started.");
    }

    public void Stop(string reason = "stopping")
    {
        _running = false;
        foreach (var connection in _connections.Values)
        {
            connection.Disconnect(reason);
        }
        _connections.Clear();
        ApplicationContext.Context.Value?.Logger.LogInformation("WebSocket network interface stopped.");
    }

    public bool Connect() => true; // Server-side, we accept connections

    public void Disconnect(IConnection connection, string message)
    {
        if (connection is WebSocketConnection wsConnection)
        {
            wsConnection.Disconnect(message);
        }
    }

    public void Disconnect(ICollection<IConnection> connections, string messages)
    {
        foreach (var connection in connections)
        {
            Disconnect(connection, messages);
        }
    }

    public void Dispose()
    {
        Stop("disposing");
    }
}
