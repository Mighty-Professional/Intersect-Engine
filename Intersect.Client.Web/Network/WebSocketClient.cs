using Intersect.Client.Core;
using Intersect.Client.Framework.Network;
using Intersect.Client.Interface.Menu;
using Intersect.Configuration;
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
    private bool _statusCheckStarted;
    private CancellationTokenSource? _pollCts;

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
        var locationProtocol = _js.Invoke<string>("IntersectWebHelper.getLocationProtocol");
        var protocol = locationProtocol == "https:" ? "wss" : "ws";
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
        // Poll server status via HTTP API (replaces LiteNetLib unconnected packets)
        if (!_statusCheckStarted)
        {
            _statusCheckStarted = true;
            _pollCts = new CancellationTokenSource();
            _ = PollServerStatusAsync(_pollCts.Token);
        }

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

        // Process incoming messages one at a time
        while (_js.Invoke<int>("IntersectWebSocket.getQueueLength") > 0)
        {
            var message = _js.Invoke<string?>("IntersectWebSocket.getNextMessage");

            if (message == null)
            {
                break; // No more messages
            }

            if (message.Length == 0)
            {
                // Empty string signals disconnection
                if (_connected)
                {
                    _connected = false;
                    OnDisconnected(null!, new ConnectionEventArgs());
                }
                continue;
            }

            try
            {
                byte[] messageData;
                try
                {
                    messageData = Convert.FromBase64String(message);
                }
                catch (FormatException)
                {
                    Console.Error.WriteLine($"[WS:DESER] Invalid base64 data ({message.Length} chars), skipping");
                    continue;
                }
                var deserialized = MessagePacker.Instance.Deserialize(messageData);
                if (deserialized is IntersectPacket intersectPacket)
                {
                    OnDataReceived(intersectPacket);
                }
                else
                {
                    Console.Error.WriteLine($"[WS:DESER] Deserialized to non-IntersectPacket: {deserialized?.GetType().Name ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WS:DESER] FAILED ({message.Length} b64 chars, ~{message.Length * 3 / 4} bytes): {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine($"[WS:DESER] Stack: {ex.StackTrace?.Substring(0, Math.Min(500, ex.StackTrace?.Length ?? 0))}");
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
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        Disconnect("disposed");
    }

    /// <summary>
    /// Polls the server's REST API to determine if it's online.
    /// Replaces LiteNetLib unconnected UDP packets which aren't available over WebSocket.
    /// </summary>
    private async Task PollServerStatusAsync(CancellationToken ct)
    {
        var host = ClientConfiguration.Instance.Host;
        var port = ClientConfiguration.Instance.Port;

        var locationProtocol = _js.Invoke<string>("IntersectWebHelper.getLocationProtocol");
        var scheme = locationProtocol == "https:" ? "https" : "http";
        var apiUrl = $"{scheme}://{host}:{port}/api/v1/info";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var isOnline = await ((IJSRuntime)_js).InvokeAsync<bool>(
                    "IntersectWebSocket.checkServerStatus", ct, apiUrl);

                MainMenu.SetNetworkStatus(isOnline ? NetworkStatus.Online : NetworkStatus.Offline);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                MainMenu.SetNetworkStatus(NetworkStatus.Offline);
            }

            try
            {
                await Task.Delay(5000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
