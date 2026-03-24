using Intersect.Client.Core;
using Intersect.Client.Framework.Network;
using Intersect.Network;
using Intersect.Network.Events;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Network;

/// <summary>
/// WebSocket-based network client for browser environment.
/// Uses MessagePack serialization (same as LiteNetLib transport) over WebSocket binary frames.
/// </summary>
public class WebSocketClient : GameSocket
{
    private readonly IJSInProcessRuntime _js;
    private readonly IClientContext _context;
    private bool _connected;
    private int _ping;
    private bool _connecting;

    public WebSocketClient(IJSRuntime js, IClientContext context)
    {
        _js = (IJSInProcessRuntime)js;
        _context = context;
    }

    public override bool IsConnected => _connected;

    public override int Ping => _ping;

    public override INetwork Network => throw new NotSupportedException(
        "WebSocket client does not expose INetwork. Use GameSocket methods directly.");

    public override void Connect(string host, int port)
    {
        if (_connected || _connecting) return;
        _connecting = true;

        // Determine WebSocket URL (use wss:// if page is served over https://)
        var isSecure = _js.Invoke<bool>("eval", "location.protocol === 'https:'");
        var protocol = isSecure ? "wss" : "ws";
        var wsUrl = $"{protocol}://{host}:{port}/ws";
        Console.WriteLine($"WebSocket connecting to: {wsUrl}");

        _ = ConnectAsync(wsUrl);
    }

    private async Task ConnectAsync(string wsUrl)
    {
        try
        {
            var result = await ((IJSRuntime)_js).InvokeAsync<bool>(
                "IntersectWebSocket.connect", wsUrl);

            if (result)
            {
                _connected = true;
                _connecting = false;
                Console.WriteLine("WebSocket connection established");

                // Fire the Connected event
                OnConnected(null!, new ConnectionEventArgs());
            }
            else
            {
                _connecting = false;
                Console.Error.WriteLine("WebSocket connection returned false");
                OnConnectionFailed(null!, new ConnectionEventArgs(), false);
            }
        }
        catch (Exception ex)
        {
            _connecting = false;
            _connected = false;
            Console.Error.WriteLine($"WebSocket connection failed: {ex.Message}");
            OnConnectionFailed(null!, new ConnectionEventArgs(), false);
        }
    }

    public override void SendPacket(object packet)
    {
        if (!_connected) return;

        if (packet is not IntersectPacket intersectPacket)
        {
            Console.Error.WriteLine($"Cannot send non-IntersectPacket: {packet?.GetType().Name}");
            return;
        }

        try
        {
            // Serialize using MessagePack (same format as LiteNetLib transport)
            var data = MessagePacker.Instance.Serialize(intersectPacket);
            _js.InvokeVoid("IntersectWebSocket.send", data);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to send packet: {ex.Message}");
        }
    }

    public override void Update()
    {
        if (!_connected && !_connecting) return;

        // Check connection status
        if (_connected)
        {
            var stillConnected = _js.Invoke<bool>("IntersectWebSocket.isConnected");
            if (!stillConnected)
            {
                _connected = false;
                Console.WriteLine("WebSocket disconnected (detected in Update)");
                OnDisconnected(null!, new ConnectionEventArgs());
                return;
            }

            _ping = _js.Invoke<int>("IntersectWebSocket.getPing");
        }

        // Process incoming messages
        var queueLength = _js.Invoke<int>("IntersectWebSocket.getQueueLength");
        if (queueLength <= 0) return;

        var messages = _js.Invoke<byte[][]?>("IntersectWebSocket.getMessages");
        if (messages == null) return;

        foreach (var messageData in messages)
        {
            if (messageData == null)
            {
                // Null signals disconnection
                if (_connected)
                {
                    _connected = false;
                    OnDisconnected(null!, new ConnectionEventArgs());
                }
                continue;
            }

            try
            {
                var deserialized = MessagePacker.Instance.Deserialize(messageData);
                if (deserialized is IntersectPacket intersectPacket)
                {
                    OnDataReceived(intersectPacket);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to deserialize packet ({messageData.Length} bytes): {ex.Message}");
            }
        }
    }

    public override void Disconnect(string reason)
    {
        if (!_connected && !_connecting) return;
        _connected = false;
        _connecting = false;

        try
        {
            _js.InvokeVoid("IntersectWebSocket.disconnect");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during disconnect: {ex.Message}");
        }

        Console.WriteLine($"WebSocket disconnected: {reason}");
    }

    public override void Dispose()
    {
        Disconnect("disposed");
    }
}
