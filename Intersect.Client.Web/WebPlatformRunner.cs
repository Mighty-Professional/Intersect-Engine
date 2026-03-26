using Intersect.Client.Core;
using Intersect.Client.Framework.Database;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.Framework.Gwen.Input;
using Intersect.Client.Framework.Gwen.Renderer;
using Intersect.Client.Framework.Input;
using Intersect.Client.General;
using Intersect.Client.Interface.Shared;
using Intersect.Client.Networking;
using Intersect.Configuration;
using Intersect.Enums;
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
    private int _autoLoginState;
    private string? _autoUser;
    private string? _autoPass;
    private int _consecutiveErrors;
    private string? _lastErrorMessage;
    private int _frameNumber;

    /// <summary>
    /// Parameterless constructor for assembly scanning by ClientContext.
    /// </summary>
    public WebPlatformRunner() { }

    /// <inheritdoc />
    public void Start(IClientContext context, Action postStartupAction)
    {
        WebDebugLog.Log("INIT", "Start() called");
        var js = JsRuntime ?? throw new InvalidOperationException(
            "JsRuntime must be set before Start is called");

        _context = context;

        // Initialize all subsystems
        WebDebugLog.Log("INIT", "Creating subsystems...");
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
        WebDebugLog.Log("INIT", "Globals wired up");

        // Set asset base URL to the game server's HTTP endpoint.
        // The web client may be served from a different origin (e.g. Blazor dev server on :5000)
        // but resources are served by the game server (e.g. :5400).
        var isSecure = js.Invoke<bool>("eval", "location.protocol === 'https:'");
        var httpProtocol = isSecure ? "https" : "http";
        var host = ClientConfiguration.Instance.Host;
        var port = ClientConfiguration.Instance.Port;
        contentManager.AssetBaseUrl = $"{httpProtocol}://{host}:{port}/resources";
        WebDebugLog.Log("INIT", $"AssetBaseUrl = {contentManager.AssetBaseUrl}");

        // Initialize renderer
        WebDebugLog.Log("INIT", "Initializing renderer...");
        renderer.Init();
        WebDebugLog.Log("INIT", $"Renderer initialized: {renderer.ScreenWidth}x{renderer.ScreenHeight}");

        // Set up GWEN UI
        WebDebugLog.Log("INIT", "Setting up GWEN UI...");
        Interface.Interface.GwenRenderer = new IntersectRenderer(null, Core.Graphics.Renderer);
        Interface.Interface.GwenInput = new IntersectInput();
        WebDebugLog.Log("INIT", "GWEN UI ready");

        // Set up networking
        WebDebugLog.Log("INIT", "Setting up networking...");
        Networking.Network.Socket = new WebSocketClient(js, context);

        // Initialize game
        WebDebugLog.Log("INIT", "Calling Main.Start()...");
        Main.Start(context);
        WebDebugLog.Log("INIT", "Main.Start() complete");

        postStartupAction();
        _initialized = true;
        _lastFrameTime = DateTime.UtcNow;

        WebDebugLog.Log("INIT", $"Initialization complete. GameState={Globals.GameState}, IsRunning={Globals.IsRunning}");

        // Start the frame loop (non-blocking in WASM single-threaded environment)
        _ = RunFrameLoopAsync(js);
    }

    private void LogFrameError(string phase, Exception ex)
    {
        _consecutiveErrors++;
        var msg = $"{phase}: {ex.GetType().Name}: {ex.Message}";
        if (msg != _lastErrorMessage)
        {
            Console.Error.WriteLine($"Frame error ({_consecutiveErrors}): {phase}: {ex}");
            _lastErrorMessage = msg;
        }
        else if (_consecutiveErrors % 100 == 0)
        {
            Console.Error.WriteLine($"Frame error repeated {_consecutiveErrors}x: {msg}");
        }
    }

    private void ProcessAutoLogin(IJSInProcessRuntime js)
    {
        if (_autoLoginState == 0)
        {
            var param = js.Invoke<string?>("eval", "new URLSearchParams(location.search).get('autologin')");
            if (string.IsNullOrEmpty(param)) { _autoLoginState = -1; return; }
            var parts = param.Split(':');
            if (parts.Length != 2) { _autoLoginState = -1; return; }
            _autoUser = parts[0];
            _autoPass = parts[1];
            _autoLoginState = 1;
        }
        else if (_autoLoginState == 1)
        {
            if (Networking.Network.Socket == null || !Networking.Network.Socket.IsConnected || Globals.WaitingOnServer) return;
            PacketSender.SendLogin(_autoUser!, _autoPass!);
            Globals.WaitingOnServer = true;
            _autoLoginState = 2;
        }
        else if (_autoLoginState == 2)
        {
            if (Globals.WaitingOnServer) return;
            if (Globals.GameState == GameStates.InGame || Globals.GameState == GameStates.Loading) { _autoLoginState = -1; return; }
            if (!Interface.Interface.HasMainMenuUI) return;
            try
            {
                var menuUi = Interface.Interface.MenuUi;
                var field = menuUi.GetType().GetField("_selectCharacterWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var charSelect = field?.GetValue(menuUi);
                var prop = charSelect?.GetType().GetProperty("CharacterSelectionPreviews");
                if (prop?.GetValue(charSelect) is Array previews && previews.Length > 0)
                {
                    var id = (Guid)(previews.GetValue(0)!.GetType().GetProperty("Id")!.GetValue(previews.GetValue(0))!);
                    PacketSender.SendSelectCharacter(id);
                    Globals.WaitingOnServer = true;
                    _autoLoginState = -1;
                }
            }
            catch { _autoLoginState = -1; }
        }
    }

    private async Task RunFrameLoopAsync(IJSInProcessRuntime js)
    {
        WebDebugLog.Log("LOOP", "Frame loop starting");
        while (_initialized && Globals.IsRunning)
        {
            _frameNumber++;
            var now = DateTime.UtcNow;
            var elapsed = now - _lastFrameTime;
            _lastFrameTime = now;

            // Log first 10 frames and every 300th frame after
            var logThisFrame = _frameNumber <= 10 || _frameNumber % 300 == 0;

            if (logThisFrame)
            {
                WebDebugLog.Log("LOOP", $"Frame {_frameNumber}: GameState={Globals.GameState}, elapsed={elapsed.TotalMilliseconds:F1}ms");
            }

            try
            {
                // Auto-login for testing (?autologin=user:pass)
                if (_autoLoginState >= 0) ProcessAutoLogin(js);

                // Update input state (synchronous in WASM)
                js.InvokeVoid("IntersectInput.update");
            }
            catch (Exception ex)
            {
                LogFrameError("Input", ex);
            }

            try
            {
                // Update game logic (no lock needed - WASM is single-threaded)
                Main.Update(elapsed);
            }
            catch (Exception ex)
            {
                LogFrameError("Update", ex);
            }

            try
            {
                // Render (always attempt, even if Update failed)
                Core.Graphics.Render(elapsed, TimeSpan.Zero);
                _consecutiveErrors = 0;
                _lastErrorMessage = null;
            }
            catch (Exception ex)
            {
                LogFrameError("Render", ex);
            }

            // Yield to browser so it can paint the canvas
            await Task.Delay(1);
        }
        WebDebugLog.Log("LOOP", $"Frame loop exited. _initialized={_initialized}, IsRunning={Globals.IsRunning}");
    }
}
