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
using Microsoft.JSInterop;

namespace Intersect.Client.Web;

/// <summary>
/// Web platform runner implementing IPlatformRunner for Blazor WASM.
/// Uses an async frame loop instead of a blocking Game.Run().
/// </summary>
public class WebPlatformRunner : IPlatformRunner
{
    internal static WebPlatformRunner? Instance { get; set; }
    internal static IJSInProcessRuntime? JsRuntime { get; set; }

    private IClientContext? _context;
    private bool _initialized;
    private DateTime _lastFrameTime;

    /// <summary>
    /// Parameterless constructor for assembly scanning by ClientContext.
    /// </summary>
    public WebPlatformRunner() { }

    /// <inheritdoc />
    public void Start(IClientContext context, Action postStartupAction)
    {
        var js = JsRuntime ?? throw new InvalidOperationException(
            "JsRuntime must be set before Start is called");

        _context = context;

        // Initialize all subsystems
        var renderer = new WebRenderer(js);
        var input = new WebInput(js);
        var clipboard = new WebClipboard(js);
        var contentManager = new WebContentManager(js);
        var database = new WebDatabase(js);

        // Wire up globals
        Globals.InputManager = input;
        GameClipboard.Instance = clipboard;
        Globals.ContentManager = contentManager;
        Globals.Database = database;
        Core.Graphics.Renderer = renderer;

        // Initialize renderer
        renderer.Init();

        // Set up GWEN UI
        Interface.Interface.GwenRenderer = new IntersectRenderer(null, Core.Graphics.Renderer);
        Interface.Interface.GwenInput = new IntersectInput();

        // Set up networking
        Networking.Network.Socket = new WebSocketClient(js, context);

        // Initialize game
        Main.Start(context);
        postStartupAction();
        _initialized = true;
        _lastFrameTime = DateTime.UtcNow;

        // Start the frame loop (non-blocking in WASM single-threaded environment)
        _ = RunFrameLoopAsync(js);
    }

    private async Task RunFrameLoopAsync(IJSInProcessRuntime js)
    {
        while (_initialized && Globals.IsRunning)
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastFrameTime;
            _lastFrameTime = now;

            try
            {
                // Update input state (synchronous in WASM)
                js.InvokeVoid("IntersectInput.update");

                // Update game logic (no lock needed - WASM is single-threaded)
                Main.Update(elapsed);

                // Render
                Core.Graphics.Render(elapsed, TimeSpan.Zero);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Frame error: {ex}");
            }

            // Yield to browser via Task.Delay - gives browser time to paint
            await Task.Delay(1);
        }
    }
}
