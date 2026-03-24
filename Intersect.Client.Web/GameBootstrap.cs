using System.Globalization;
using Microsoft.JSInterop;

namespace Intersect.Client.Web;

/// <summary>
/// Bootstraps the Intersect game client in a web browser environment.
/// Initializes JS subsystems and stores the runtime for the platform runner.
/// </summary>
public static class GameBootstrap
{
    public static async Task StartAsync(IJSRuntime jsRuntime)
    {
        if (jsRuntime is not IJSInProcessRuntime jsSync)
        {
            throw new InvalidOperationException(
                "Intersect Web Client requires IJSInProcessRuntime (Blazor WebAssembly only, not Blazor Server)");
        }

        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");

        // Initialize JS subsystems
        await jsRuntime.InvokeVoidAsync("IntersectWebGL.init", "game-canvas");
        await jsRuntime.InvokeVoidAsync("IntersectInput.init", "game-canvas");
        await jsRuntime.InvokeVoidAsync("IntersectAudio.init");

        // Resize canvas to window
        var canvasSize = await jsRuntime.InvokeAsync<CanvasSize>("IntersectWebGL.getCanvasSize");
        await jsRuntime.InvokeVoidAsync("IntersectWebGL.resize", canvasSize.Width, canvasSize.Height);

        // Store JS runtime for WebPlatformRunner (will be discovered via assembly scanning)
        WebPlatformRunner.JsRuntime = jsSync;

        Console.WriteLine($"Intersect Web Client initialized. Canvas: {canvasSize.Width}x{canvasSize.Height}");

        // Now bootstrap the game engine - this will discover WebPlatformRunner and call Start()
        var entryAssembly = typeof(GameBootstrap).Assembly;
        Intersect.Client.Core.Bootstrapper.Start(entryAssembly, Array.Empty<string>());
    }

    private record CanvasSize(int Width, int Height);
}
