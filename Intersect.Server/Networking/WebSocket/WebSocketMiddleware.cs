using Microsoft.AspNetCore.Http;

namespace Intersect.Server.Networking.WebSocket;

/// <summary>
/// ASP.NET Core middleware that accepts WebSocket connections at /ws
/// and bridges them to the game server's networking layer via WebSocketNetworkInterface.
/// </summary>
public sealed class WebSocketMiddleware
{
    private readonly RequestDelegate _next;

    public WebSocketMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.Equals("/ws", StringComparison.OrdinalIgnoreCase) || !context.WebSockets.IsWebSocketRequest)
        {
            await _next(context);
            return;
        }

        var networkInterface = context.RequestServices.GetService<WebSocketNetworkInterface>();
        if (networkInterface == null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var port = context.Connection.RemotePort;

        var connection = networkInterface.AcceptConnection(socket, ip, port);
        if (connection == null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // Keep the middleware alive while the WebSocket connection is open.
        // The receive loop runs inside WebSocketConnection.HandleConnected().
        var tcs = new TaskCompletionSource();

        _ = Task.Run(async () =>
        {
            try
            {
                while (connection.IsConnected)
                {
                    await Task.Delay(1000);
                }
            }
            catch
            {
                // Ensure we always complete the TCS
            }
            tcs.TrySetResult();
        });

        await tcs.Task;
    }
}
