using System.Net.WebSockets;
using System.Threading.Channels;
using Intersect.Network;

namespace Intersect.Server.Networking.WebSocket;

/// <summary>
/// Represents a single WebSocket client connection that implements IConnection
/// for integration with the game server's networking layer.
/// </summary>
public sealed class WebSocketConnection : IConnection
{
    private const int ReceiveBufferSize = 256 * 1024; // 256KB for large game packets
    private const int MaxMessageSize = 1024 * 1024; // 1MB max inbound message size
    private const int SendQueueCapacity = 1024;

    private readonly System.Net.WebSockets.WebSocket _socket;
    private readonly WebSocketNetworkInterface _interface;
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<byte[]> _sendChannel = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(SendQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    private int _disposed;
    private int _disconnected;

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

            if (!_sendChannel.Writer.TryWrite(data))
            {
                return false;
            }

            Statistics.SentPackets++;
            Statistics.SentBytes += data.Length;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WS:SEND] Failed to enqueue {packet.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private async Task SendLoopAsync()
    {
        try
        {
            await foreach (var data in _sendChannel.Reader.ReadAllAsync(_cts.Token))
            {
                await _socket.SendAsync(
                    new ArraySegment<byte>(data),
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    _cts.Token
                );
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception)
        {
            Disconnect("Send failed");
        }
    }

    public void HandleConnected()
    {
        _ = SendLoopAsync();
        _ = ReceiveLoopAsync();
    }

    public void HandleApproved() { }

    public void HandleDisconnected()
    {
        _cts.Cancel();
    }

    public void Disconnect(string? message = default)
    {
        if (Interlocked.Exchange(ref _disconnected, 1) != 0) return;

        _sendChannel.Writer.TryComplete();

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
        var buffer = new byte[ReceiveBufferSize];
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

                    if (ms.Length > MaxMessageSize)
                    {
                        Disconnect("Message too large");
                        return;
                    }
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        _cts.Dispose();
        _socket.Dispose();
    }
}
