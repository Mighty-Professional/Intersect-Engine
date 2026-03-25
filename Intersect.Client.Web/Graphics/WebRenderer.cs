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

    // Text cache to avoid recreating textures every frame (M1 fix)
    private readonly Dictionary<string, (int textureId, int width, int height, long lastUsed)> _textCache = new();
    private const int TextCacheMaxSize = 256;

    public WebRenderer(IJSInProcessRuntime js)
    {
        _js = js;
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
        // Sync renderer dimensions with actual canvas size
        var canvasSize = _js.Invoke<CanvasSizeResult>("IntersectWebGL.getCanvasSize");
        _screenWidth = canvasSize.Width;
        _screenHeight = canvasSize.Height;
        _js.InvokeVoid("IntersectWebGL.resize", _screenWidth, _screenHeight);

        _whitePixel = CreateWhitePixel();
    }

    private record CanvasSizeResult(int Width, int Height);

    public override bool Begin()
    {
        _js.InvokeVoid("IntersectWebGL.beginFrame");
        return true;
    }

    public override bool BeginScreenshot() => true;

    protected override bool RecreateSpriteBatch() => true;

    protected override void DoEnd()
    {
        _js.InvokeVoid("IntersectWebGL.endFrame");

        // FPS calculation
        _frameCount++;
        var now = DateTime.UtcNow;
        if ((now - _lastFpsTime).TotalSeconds >= 1.0)
        {
            _fps = _frameCount;
            _frameCount = 0;
            _lastFpsTime = now;
        }

        // Prune text cache
        if (_textCache.Count > TextCacheMaxSize)
        {
            var cutoff = Environment.TickCount64 - 5000;
            var stale = _textCache.Where(kv => kv.Value.lastUsed < cutoff).Select(kv => kv.Key).ToList();
            foreach (var key in stale)
            {
                _js.InvokeVoid("IntersectWebGL.deleteTexture", _textCache[key].textureId);
                _textCache.Remove(key);
            }
        }
    }

    public override void EndScreenshot() { }

    public override void SetView(FloatRect view)
    {
        _currentView = view;
        _js.InvokeVoid("IntersectWebGL.setView", view.X, view.Y, view.Width, view.Height);
    }

    public override FloatRect GetView() => _currentView;

    public override IFont LoadFont(string fontName, IDictionary<int, FileInfo> fontSourcesBySize)
    {
        return new WebFont(_js, fontName, fontSourcesBySize.Keys);
    }

    public override void DrawTexture(
        IGameTexture tex, float sx, float sy, float sw, float sh,
        float tx, float ty, float tw, float th,
        Color renderColor, IGameRenderTexture? renderTarget = null,
        GameBlendModes blendMode = GameBlendModes.None, GameShader? shader = null,
        float rotationDegrees = 0.0f, bool isUi = false, bool drawImmediate = false)
    {
        if (tex is not WebTexture webTex) return;
        var texId = webTex.PlatformTextureId;
        if (texId <= 0) return;

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

        _js.InvokeVoid("IntersectWebGL.drawTexture",
            texId, sx, sy, sw, sh, tx, ty, tw, th,
            renderColor.R, renderColor.G, renderColor.B, renderColor.A,
            blendModeInt);
    }

    public override void DrawString(string text, IFont? gameFont, int size, float x, float y,
        float fontScale, Color? fontColor, bool worldPos = true,
        IGameRenderTexture? renderTexture = null, Color? borderColor = null)
    {
        if (string.IsNullOrEmpty(text) || gameFont == null) return;
        var color = fontColor ?? Color.White;
        var fontSize = (int)(size * fontScale);
        if (fontSize <= 0) return;

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
            return;
        }

        var result = _js.Invoke<TextRenderResult?>(
            "IntersectWebGL.renderTextToTexture",
            text, gameFont.Name, fontSize,
            color.R, color.G, color.B, color.A,
            bc.R, bc.G, bc.B, bc.A);

        if (result == null) return;

        _textCache[cacheKey] = (result.TextureId, result.Width, result.Height, Environment.TickCount64);

        _js.InvokeVoid("IntersectWebGL.drawTexture",
            result.TextureId, 0f, 0f, (float)result.Width, (float)result.Height,
            x, y, (float)result.Width, (float)result.Height,
            255, 255, 255, 255, 0);
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
        if (vertexBuffer is not WebVertexBuffer wvb) return;
        var wib = indexBuffer as WebIndexBuffer;

        // Get texture ID from the active shader's texture
        var textureId = 0;
        if (ActiveShader?.Texture is WebTexture webTex)
            textureId = webTex.PlatformTextureId;

        // Determine index type: 0 = ushort, 1 = uint
        var indexType = 0;
        if (wib?.IndexType == typeof(uint) || wib?.IndexType == typeof(int))
            indexType = 1;

        _js.InvokeVoid("IntersectWebGL.drawBuffers",
            wvb.PlatformBufferId, wib?.PlatformBufferId ?? 0, textureId,
            wib?.Count ?? 0, 0, indexType);
    }

    public override void Close()
    {
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
