using System.Net.WebSockets;
using Intersect.Network;

namespace Intersect.Server.Networking.WebSocket;

/// <summary>
/// Represents a single WebSocket client connection that implements IConnection
/// for integration with the game server's networking layer.
/// </summary>
public sealed class WebSocketConnection : IConnection
{
    private readonly System.Net.WebSockets.WebSocket _socket;
    private readonly WebSocketNetworkInterface _interface;
    private readonly CancellationTokenSource _cts = new();

    public WebSocketConnection(
        System.Net.WebSockets.WebSocket socket,
        WebSocketNetworkInterface networkInterface,
        string ip,
        int port)
    {
        _socket = socket;
        _interface = networkInterface;
        Guid = Guid.NewGuid();
        Ip = ip;
        Port = port;
        Statistics = new ConnectionStatistics();
    }

    public Guid Guid { get; }
    public bool IsConnected => _socket.State == WebSocketState.Open;
    public string Ip { get; }
    public int Port { get; }
    public ConnectionStatistics Statistics { get; }

    public bool Send(IPacket packet, TransmissionMode mode = TransmissionMode.All)
    {
        if (_socket.State != WebSocketState.Open) return false;

        try
        {
            var data = packet.Data;
            if (data == null || data.Length == 0) return false;

            // Log packet sizes for debugging (especially large packets like GameDataPacket)
            var packetTypeName = packet.GetType().Name;
            if (data.Length > 1000 || Statistics.SentPackets < 20)
            {
                Console.WriteLine($"[WS:SEND] {packetTypeName}: {data.Length} bytes to {Ip}");
            }

            // Send synchronously (fire-and-forget async internally)
            _ = SendAsync(data);

            Statistics.SentPackets++;
            Statistics.SentBytes += data.Length;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WS:SEND] Failed to send {packet.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private async Task SendAsync(byte[] data)
    {
        try
        {
            await _socket.SendAsync(
                new ArraySegment<byte>(data),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                _cts.Token
            );
        }
        catch (Exception)
        {
            // Connection closed during send
            Disconnect("Send failed");
        }
    }

    public void HandleConnected()
    {
        // Start the receive loop
        _ = ReceiveLoopAsync();
    }

    public void HandleApproved() { }

    public void HandleDisconnected()
    {
        _cts.Cancel();
    }

    public void Disconnect(string? message = default)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                _ = _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    message ?? "Server disconnect",
                    CancellationToken.None
                );
            }
            catch
            {
                // Already closing
            }
        }

        _cts.Cancel();
        _interface.HandleDisconnection(this);
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[256 * 1024]; // 256KB receive buffer for large packets
        try
        {
            while (_socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        _cts.Token
                    );

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Disconnect("Client closed connection");
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Binary && ms.Length > 0)
                {
                    Statistics.ReceivedPackets++;
                    Statistics.ReceivedBytes += ms.Length;
                    _interface.EnqueueInboundData(this, ms.ToArray());
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (WebSocketException)
        {
            Disconnect("WebSocket error");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _socket.Dispose();
    }
}
