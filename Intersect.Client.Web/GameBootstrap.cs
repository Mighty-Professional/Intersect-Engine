using System.Globalization;
using System.Reflection;
using Intersect.Client.Core;
using Intersect.Client.Web.Graphics;
using Intersect.Client.Web.Input;
using Intersect.Client.Web.Audio;
using Intersect.Client.Web.Content;
using Intersect.Client.Web.Database;
using Intersect.Client.Web.Network;
using Intersect.Configuration;
using Intersect.Core;
using Intersect.Factories;
using Intersect.Network;
using Intersect.Plugins;
using Intersect.Plugins.Contexts;
using Intersect.Plugins.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Intersect.Client.Web;

/// <summary>
/// Bootstraps the Intersect game client in a web browser environment.
/// </summary>
public static class GameBootstrap
{
    private static IJSRuntime? _jsRuntime;

    public static IJSRuntime JsRuntime =>
        _jsRuntime ?? throw new InvalidOperationException("JS Runtime not initialized");

    public static async Task StartAsync(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;

        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");

        // Initialize JS subsystems
        await jsRuntime.InvokeVoidAsync("IntersectWebGL.init", "game-canvas");
        await jsRuntime.InvokeVoidAsync("IntersectInput.init", "game-canvas");
        await jsRuntime.InvokeVoidAsync("IntersectAudio.init");

        // Resize canvas to window
        var canvasSize = await jsRuntime.InvokeAsync<CanvasSize>("IntersectWebGL.getCanvasSize");
        await jsRuntime.InvokeVoidAsync("IntersectWebGL.resize", canvasSize.Width, canvasSize.Height);

        // Start the game via the platform runner
        var runner = new WebPlatformRunner(jsRuntime);

        // Register the runner so ClientContext can find it
        WebPlatformRunner.Instance = runner;

        // For now, log that we're ready - full bootstrapper integration will follow
        Console.WriteLine($"Intersect Web Client initialized. Canvas: {canvasSize.Width}x{canvasSize.Height}");
    }

    private record CanvasSize(int Width, int Height);
}
