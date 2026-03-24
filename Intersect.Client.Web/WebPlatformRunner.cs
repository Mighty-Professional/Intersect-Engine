using Intersect.Client.Core;
using Intersect.Client.Framework.Database;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.Framework.Gwen.Input;
using Intersect.Client.Framework.Gwen.Renderer;
using Intersect.Client.Framework.Input;
using Intersect.Client.General;
using Intersect.Client.Interface.Shared;
using Intersect.Client.Web.Audio;
using Intersect.Client.Web.Content;
using Intersect.Client.Web.Database;
using Intersect.Client.Web.Graphics;
using Intersect.Client.Web.Input;
using Intersect.Client.Web.Network;
using Intersect.Utilities;
using Microsoft.JSInterop;

namespace Intersect.Client.Web;

/// <summary>
/// Web platform runner implementing IPlatformRunner for Blazor WASM.
/// Uses requestAnimationFrame for the game loop instead of a blocking Game.Run().
/// </summary>
public class WebPlatformRunner : IPlatformRunner
{
    internal static WebPlatformRunner? Instance { get; set; }

    private readonly IJSRuntime _js;
    private IClientContext? _context;
    private Action? _postStartupAction;
    private bool _initialized;
    private DateTime _lastFrameTime;

    public WebPlatformRunner(IJSRuntime jsRuntime)
    {
        _js = jsRuntime;
    }

    /// <inheritdoc />
    public void Start(IClientContext context, Action postStartupAction)
    {
        _context = context;
        _postStartupAction = postStartupAction;

        // Initialize all subsystems
        var renderer = new WebRenderer(_js);
        var input = new WebInput(_js);
        var clipboard = new WebClipboard(_js);
        var contentManager = new WebContentManager(_js);
        var database = new WebDatabase(_js);

        // Wire up globals
        Globals.InputManager = input;
        GameClipboard.Instance = clipboard;
        Globals.ContentManager = contentManager;
        Globals.Database = database;
        Core.Graphics.Renderer = renderer;

        // Set up GWEN UI
        Interface.Interface.GwenRenderer = new IntersectRenderer(null, Core.Graphics.Renderer);
        Interface.Interface.GwenInput = new IntersectInput();

        // Set up networking
        Networking.Network.Socket = new WebSocketClient(_js, context);

        // Initialize game
        Main.Start(context);
        _postStartupAction?.Invoke();
        _initialized = true;
        _lastFrameTime = DateTime.UtcNow;

        // Start the frame loop - in web, this is non-blocking via requestAnimationFrame
        _ = RunFrameLoopAsync();
    }

    private async Task RunFrameLoopAsync()
    {
        while (_initialized && Globals.IsRunning)
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastFrameTime;
            _lastFrameTime = now;

            try
            {
                // Update input state
                await _js.InvokeVoidAsync("IntersectInput.update");

                // Update game logic
                lock (Globals.GameLock)
                {
                    Main.Update(elapsed);
                }

                // Render
                await _js.InvokeVoidAsync("IntersectWebGL.beginFrame");
                Core.Graphics.Render(elapsed, TimeSpan.Zero);
                await _js.InvokeVoidAsync("IntersectWebGL.endFrame");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Frame error: {ex.Message}");
            }

            // Yield to browser - ~16ms for 60fps
            await Task.Delay(16);
        }
    }
}
