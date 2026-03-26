using System.Numerics;
using Intersect.Client.Framework.Content;
using Intersect.Client.Framework.GenericClasses;
using Intersect.Client.Framework.Graphics;
using Intersect.Framework.Core;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Graphics;

/// <summary>
/// WebGL2-based renderer implementing GameRenderer via JS interop.
/// </summary>
public partial class WebRenderer : GameRenderer
{
    private readonly IJSInProcessRuntime _js;
    private int _screenWidth = 800;
    private int _screenHeight = 600;
    private int _fps;
    private int _frameCount;
    private DateTime _lastFpsTime = DateTime.UtcNow;
    private WebShader? _basicShader;
    private FloatRect _currentView;
    private IGameTexture? _whitePixel;
    private int _drawCallsThisFrame;
    private int _totalDrawCalls;
    private int _renderFrameNumber;

    // Text cache to avoid recreating textures every frame (M1 fix)
    private readonly Dictionary<string, (int textureId, int width, int height, long lastUsed)> _textCache = new();
    private const int TextCacheMaxSize = 256;

    public WebRenderer(IJSInProcessRuntime js)
    {
        _js = js;
        WebDebugLog.Log("RENDERER", "WebRenderer created");
    }

    // Abstract property implementations
    public override GameShader BasicShader => _basicShader ??= new WebShader(_js, "basic");
    public override long UsedMemory => 0; // WebGL manages its own memory
    public override int FPS => _fps;
    public override List<string> ValidVideoModes => [$"{_screenWidth}, {_screenHeight}"];
    public override int ScreenWidth => _screenWidth;
    public override int ScreenHeight => _screenHeight;

    // Abstract method implementations
    public override void Init()
    {
        WebDebugLog.Log("RENDERER", "Init() called");
        // Sync renderer dimensions with actual canvas size
        var canvasSize = _js.Invoke<CanvasSizeResult>("IntersectWebGL.getCanvasSize");
        _screenWidth = canvasSize.Width;
        _screenHeight = canvasSize.Height;
        WebDebugLog.Log("RENDERER", $"Canvas size: {_screenWidth}x{_screenHeight}");

        _js.InvokeVoid("IntersectWebGL.resize", _screenWidth, _screenHeight);

        _whitePixel = CreateWhitePixel();
        WebDebugLog.Log("RENDERER", $"White pixel created: {(_whitePixel as WebTexture)?.PlatformTextureId}");
        WebDebugLog.Log("RENDERER", "Init() complete");
    }

    private record CanvasSizeResult(int Width, int Height);

    public override bool Begin()
    {
        _renderFrameNumber++;
        _drawCallsThisFrame = 0;
        _js.InvokeVoid("IntersectWebGL.beginFrame");
        return true;
    }

    /// <summary>Called from the game loop to signal we're in a specific game state for logging.</summary>
    public void SetInGameMode(bool inGame)
    {
        if (inGame && !_inGameMode)
        {
            _inGameFrameCount = 0; // Reset on first entry
        }
        _inGameMode = inGame;
        if (inGame) _inGameFrameCount++;
    }

    public override bool BeginScreenshot() => true;

    protected override bool RecreateSpriteBatch()
    {
        // Re-apply the current view projection when scale changes
        // MonoGame does this via SpriteBatch.Begin with CreateViewMatrix
        if (_currentView.Width > 0 && _currentView.Height > 0)
        {
            _js.InvokeVoid("IntersectWebGL.setView", _currentView.X, _currentView.Y, _currentView.Width, _currentView.Height);
        }
        return true;
    }

    protected override void DoEnd()
    {
        // Log per-frame draw call summary for in-game frames
        if (_inGameMode && _inGameFrameCount <= 10)
        {
            WebDebugLog.Log("RENDERER", $"[FRAME SUMMARY] InGameFrame #{_inGameFrameCount}: {_drawCallsThisFrame} draw calls, view=({_currentView.X:F0},{_currentView.Y:F0},{_currentView.Width:F0},{_currentView.Height:F0})");
        }

        _js.InvokeVoid("IntersectWebGL.endFrame");
        _totalDrawCalls += _drawCallsThisFrame;

        // FPS calculation
        _frameCount++;
        var now = DateTime.UtcNow;
        if ((now - _lastFpsTime).TotalSeconds >= 1.0)
        {
            _fps = _frameCount;
            if (_renderFrameNumber <= 5 || _renderFrameNumber % 300 == 0)
            {
                WebDebugLog.Log("RENDERER", $"FPS={_fps}, DrawCalls/sec={_totalDrawCalls}, TextCache={_textCache.Count}");
            }
            _frameCount = 0;
            _totalDrawCalls = 0;
            _lastFpsTime = now;
        }

        // Prune text cache using LRU eviction when over capacity
        if (_textCache.Count > TextCacheMaxSize)
        {
            var evictCount = _textCache.Count - TextCacheMaxSize + TextCacheMaxSize / 4; // Evict 25% extra to avoid thrashing
            var toEvict = _textCache
                .OrderBy(kv => kv.Value.lastUsed)
                .Take(evictCount)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in toEvict)
            {
                if (_textCache.TryGetValue(key, out var entry))
                {
                    _js.InvokeVoid("IntersectWebGL.deleteTexture", entry.textureId);
                    _textCache.Remove(key);
                }
            }
        }
    }

    public override void EndScreenshot() { }

    public override void SetView(FloatRect view)
    {
        _currentView = view;
        _js.InvokeVoid("IntersectWebGL.setView", view.X, view.Y, view.Width, view.Height);

        if (_renderFrameNumber <= 3)
        {
            WebDebugLog.Log("RENDERER", $"SetView({view.X}, {view.Y}, {view.Width}, {view.Height})");
        }
    }

    public override FloatRect GetView() => _currentView;

    public override IFont LoadFont(string fontName, IDictionary<int, FileInfo> fontSourcesBySize)
    {
        WebDebugLog.Log("RENDERER", $"LoadFont: {fontName}");
        return new WebFont(_js, fontName, fontSourcesBySize.Keys);
    }

    // Track in-game frame count for logging
    private bool _inGameMode;
    private int _inGameFrameCount;

    public override void DrawTexture(
        IGameTexture tex, float sx, float sy, float sw, float sh,
        float tx, float ty, float tw, float th,
        Color renderColor, IGameRenderTexture? renderTarget = null,
        GameBlendModes blendMode = GameBlendModes.None, GameShader? shader = null,
        float rotationDegrees = 0.0f, bool isUi = false, bool drawImmediate = false)
    {
        // Support both WebTexture and WebRenderTexture as source textures
        int texId;
        string texName;
        bool isRenderTexture = false;
        if (tex is WebTexture webTex)
        {
            texId = webTex.PlatformTextureId;
            texName = webTex.Name;
        }
        else if (tex is WebRenderTexture wrt)
        {
            texId = (int)(wrt.GetTexture() ?? 0);
            texName = wrt.Name ?? "RenderTarget";
            isRenderTexture = true;
        }
        else
        {
            if (_renderFrameNumber <= 3 || (_inGameMode && _inGameFrameCount <= 10))
                WebDebugLog.Log("RENDERER", $"DrawTexture SKIP: tex is {tex?.GetType().Name ?? "null"}, unsupported type");
            return;
        }
        if (texId <= 0)
        {
            // Log ALL skipped textures during first 10 in-game frames
            if (_renderFrameNumber <= 3 || (_inGameMode && _inGameFrameCount <= 10))
                WebDebugLog.Log("RENDERER", $"DrawTexture SKIP: {texName} texId={texId} (not loaded) isUi={isUi}");
            return;
        }

        _drawCallsThisFrame++;

        int blendModeInt = blendMode switch
        {
            GameBlendModes.Alpha => 0,
            GameBlendModes.Multiply => 1,
            GameBlendModes.Add => 2,
            GameBlendModes.Opaque => 3,
            GameBlendModes.Cutout => 4,
            _ => 0
        };

        if (tex.AtlasReference != null)
        {
            sx += tex.AtlasReference.Bounds.X;
            sy += tex.AtlasReference.Bounds.Y;
        }

        // If drawing TO a render target, bind it and set its projection
        if (renderTarget is WebRenderTexture destRt)
        {
            var rtTexId = (int)(destRt.GetTexture() ?? 0);
            if (rtTexId > 0)
            {
                // Bind framebuffer and set projection for render target dimensions
                destRt.Begin();
            }
        }

        // UI draws use screen coordinates but the projection is in world coordinates.
        // Offset by the current view position to convert screen coords → world coords.
        if (isUi && renderTarget == null)
        {
            tx += _currentView.X;
            ty += _currentView.Y;
        }

        // Log ALL draw calls during first 10 in-game frames and first 3 menu frames
        if (_renderFrameNumber <= 3 || (_inGameMode && _inGameFrameCount <= 10))
        {
            WebDebugLog.Log("RENDERER", $"DrawTexture OK: {texName} texId={texId} src=({sx:F0},{sy:F0},{sw:F0},{sh:F0}) dst=({tx:F0},{ty:F0},{tw:F0},{th:F0}) rgba=({renderColor.R},{renderColor.G},{renderColor.B},{renderColor.A}) isUi={isUi} blend={blendMode}");
        }

        // WebGL framebuffers are Y-flipped (bottom-up). When drawing FROM a render texture,
        // flip the source V coordinates so the image appears right-side up.
        if (isRenderTexture)
        {
            sy = sh - sy;  // flip: move source origin to bottom
            sh = -sh;      // negative height = flip vertically
        }

        _js.InvokeVoid("IntersectWebGL.drawTexture",
            texId, sx, sy, sw, sh, tx, ty, tw, th,
            renderColor.R, renderColor.G, renderColor.B, renderColor.A,
            blendModeInt);

        // Unbind render target after drawing to it
        if (renderTarget is WebRenderTexture)
        {
            ((WebRenderTexture)renderTarget).End();
        }
    }

    public override void DrawString(string text, IFont? gameFont, int size, float x, float y,
        float fontScale, Color? fontColor, bool worldPos = true,
        IGameRenderTexture? renderTexture = null, Color? borderColor = null)
    {
        if (string.IsNullOrEmpty(text) || gameFont == null) return;
        var color = fontColor ?? Color.White;
        var fontSize = (int)(size * fontScale);
        if (fontSize <= 0) return;

        // UI text (worldPos=false) uses screen coordinates but projection is in world coords.
        if (!worldPos && renderTexture == null)
        {
            x += _currentView.X;
            y += _currentView.Y;
        }

        var bc = borderColor ?? new Color(0, 0, 0, 0);

        // Check text cache (M1 fix)
        var cacheKey = $"{text}|{gameFont.Name}|{fontSize}|{color.R},{color.G},{color.B},{color.A}|{bc.R},{bc.G},{bc.B},{bc.A}";
        if (_textCache.TryGetValue(cacheKey, out var cached))
        {
            _textCache[cacheKey] = cached with { lastUsed = Environment.TickCount64 };
            _js.InvokeVoid("IntersectWebGL.drawTexture",
                cached.textureId, 0f, 0f, (float)cached.width, (float)cached.height,
                x, y, (float)cached.width, (float)cached.height,
                255, 255, 255, 255, 0);
            _drawCallsThisFrame++;
            return;
        }

        var result = _js.Invoke<TextRenderResult?>(
            "IntersectWebGL.renderTextToTexture",
            text, gameFont.Name, fontSize,
            color.R, color.G, color.B, color.A,
            bc.R, bc.G, bc.B, bc.A);

        if (result == null)
        {
            if (_renderFrameNumber <= 3)
                WebDebugLog.Log("RENDERER", $"DrawString: renderTextToTexture returned null for '{text}' font={gameFont.Name} size={fontSize}");
            return;
        }

        _textCache[cacheKey] = (result.TextureId, result.Width, result.Height, Environment.TickCount64);

        _js.InvokeVoid("IntersectWebGL.drawTexture",
            result.TextureId, 0f, 0f, (float)result.Width, (float)result.Height,
            x, y, (float)result.Width, (float)result.Height,
            255, 255, 255, 255, 0);
        _drawCallsThisFrame++;
    }

    public override void DrawString(string text, IFont? gameFont, int size, float x, float y,
        float fontScale, Color fontColor, bool worldPos,
        IGameRenderTexture renderTexture, FloatRect clipRect, Color? borderColor = null)
    {
        // Skip scissor clipping for text - GWEN handles its own clipping and the
        // clip rects from IntersectRenderer are often too small due to layout timing.
        // This matches how text renders on the desktop client where scissor issues
        // are masked by different render target handling.
        DrawString(text, gameFont, size, x, y, fontScale, fontColor, worldPos, renderTexture, borderColor);
    }

    public override Vector2 MeasureText(string? text, IFont? font, int size, float fontScale)
    {
        if (string.IsNullOrEmpty(text) || font == null) return Vector2.Zero;
        var fontSize = (int)(size * fontScale);
        if (fontSize <= 0) return Vector2.Zero;

        var result = _js.Invoke<TextMeasureResult>("IntersectWebGL.measureText", text, font.Name, fontSize);
        return new Vector2(result.Width, result.Height);
    }

    public override string GetResolutionString() => $"{_screenWidth}x{_screenHeight}";

    public override bool DisplayModeChanged() => false;

    protected override IGameTexture CreateGameTextureFromAtlasReference(string assetName, AtlasReference atlasReference)
    {
        var tex = new WebTexture(_js, assetName);
        tex.AtlasReference = atlasReference;
        return tex;
    }

    public override IGameTexture CreateTextureFromStreamFactory(string assetName, Func<Stream> streamFactory)
    {
        return new WebTexture(_js, assetName, streamFactory);
    }

    protected override IGameTexture CreateWhitePixel()
    {
        var whiteId = _js.Invoke<int>("IntersectWebGL.getWhitePixelTextureId");
        return new WebTexture(_js, "__white_pixel__", whiteId, 1, 1);
    }

    public override GameTileBuffer CreateTileBuffer()
    {
        return new WebTileBuffer(_js);
    }

    public override void DrawBuffer(IVertexBuffer vertexBuffer, IIndexBuffer? indexBuffer = null)
    {
        if (vertexBuffer is not WebVertexBuffer wvb)
        {
            if (_inGameMode && _inGameFrameCount <= 10)
                WebDebugLog.Log("RENDERER", $"DrawBuffer SKIP: vertexBuffer is {vertexBuffer?.GetType().Name ?? "null"}");
            return;
        }
        var wib = indexBuffer as WebIndexBuffer;

        // Get texture ID from the active shader's texture
        var textureId = 0;
        if (ActiveShader?.Texture is WebTexture webTex)
            textureId = webTex.PlatformTextureId;

        // Determine index type: 0 = ushort, 1 = uint
        var indexType = 0;
        if (wib?.IndexType == typeof(uint) || wib?.IndexType == typeof(int))
            indexType = 1;

        var indexCount = wib?.Count ?? 0;

        // Log ALL tile buffer draws during first in-game frames
        if (_inGameMode && _inGameFrameCount <= 10)
        {
            WebDebugLog.Log("RENDERER", $"DrawBuffer: vb={wvb.PlatformBufferId} ib={wib?.PlatformBufferId ?? 0} tex={textureId} indices={indexCount}");
        }

        _js.InvokeVoid("IntersectWebGL.drawBuffers",
            wvb.PlatformBufferId, wib?.PlatformBufferId ?? 0, textureId,
            indexCount, 0, indexType);
        _drawCallsThisFrame++;
    }

    public override void Close()
    {
        WebDebugLog.Log("RENDERER", "Close() called");
        // Clean up text cache
        foreach (var entry in _textCache)
        {
            _js.InvokeVoid("IntersectWebGL.deleteTexture", entry.Value.textureId);
        }
        _textCache.Clear();
    }

    public override GameShader LoadShader(string shaderName)
    {
        return new WebShader(_js, shaderName);
    }

    public override void Clear(Color color)
    {
        if (_renderFrameNumber <= 3)
        {
            WebDebugLog.Log("RENDERER", $"Clear({color.R},{color.G},{color.B},{color.A})");
        }
        _js.InvokeVoid("IntersectWebGL.clear",
            color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }

    public override void SetActiveVertexBuffer(IVertexBuffer vertexBuffer, int bufferIndex = 0) { }

    public override IGameRenderTexture CreateRenderTexture(int width, int height)
    {
        return new WebRenderTexture(_js, width, height);
    }

    public override IIndexBuffer CreateIndexBuffer<TIndex>(int count, BufferUsage usage = BufferUsage.None, bool dynamic = false)
    {
        return new WebIndexBuffer(_js, count, typeof(TIndex), dynamic);
    }

    public override IVertexBuffer CreateVertexBuffer<TVertex>(int count, BufferUsage usage = BufferUsage.None, bool dynamic = false)
    {
        return new WebVertexBuffer(_js, count, typeof(TVertex), dynamic);
    }

    protected override void OnSetActiveShader(GameShader? shader) { }
    protected override void OnSetActiveIndexBuffer(IIndexBuffer? indexBuffer) { }
    protected override void OnCursorChanged(IGameTexture? newCursor) { }

    private record TextRenderResult(int TextureId, int Width, int Height);
    private record TextMeasureResult(int Width, int Height);
}
