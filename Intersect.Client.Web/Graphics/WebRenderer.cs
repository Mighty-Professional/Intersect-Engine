using System.Numerics;
using Intersect.Client.Framework.Content;
using Intersect.Client.Framework.GenericClasses;
using Intersect.Client.Framework.Graphics;
using Intersect.Framework.Core;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Graphics;

/// <summary>
/// WebGL2-based renderer implementing IGameRenderer via JS interop.
/// </summary>
public partial class WebRenderer : GameRenderer
{
    private readonly IJSRuntime _js;
    private int _whitePixelTextureId;
    private int _screenWidth = 800;
    private int _screenHeight = 600;
    private int _fps;
    private int _frameCount;
    private DateTime _lastFpsTime = DateTime.UtcNow;
    private WebShader? _basicShader;

    public WebRenderer(IJSRuntime js)
    {
        _js = js;
    }

    public override GameShader BasicShader => _basicShader ??= new WebShader(_js, "basic");

    public async Task InitAsync()
    {
        _whitePixelTextureId = await _js.InvokeAsync<int>("IntersectWebGL.getWhitePixelTextureId");
    }

    public override Resolution ActiveResolution => new(_screenWidth, _screenHeight);

    public override string ResolutionAsString => $"{_screenWidth}x{_screenHeight}";

    public override int ScreenWidth => _screenWidth;

    public override int ScreenHeight => _screenHeight;

    public override int FPS => _fps;

    public override List<string> ValidVideoModes => [$"{_screenWidth}x{_screenHeight}"];

    public override void Clear(Color color)
    {
        ((IJSInProcessRuntime)_js).InvokeVoid("IntersectWebGL.clear",
            color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }

    public override void DrawTexture(
        IGameTexture texture, float sourceX, float sourceY, float sourceWidth, float sourceHeight,
        float targetX, float targetY, float targetWidth, float targetHeight,
        Color renderColor, IGameRenderTexture? renderTarget = null,
        GameBlendModes blendMode = GameBlendModes.None, GameShader? shader = null,
        float rotationDegrees = 0)
    {
        if (texture is not WebTexture webTex) return;
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

        // Handle atlas references
        if (texture.AtlasReference != null)
        {
            var atlas = texture.AtlasReference;
            sourceX += atlas.X;
            sourceY += atlas.Y;
        }

        ((IJSInProcessRuntime)_js).InvokeVoid("IntersectWebGL.drawTexture",
            texId,
            sourceX, sourceY, sourceWidth, sourceHeight,
            targetX, targetY, targetWidth, targetHeight,
            renderColor.R, renderColor.G, renderColor.B, renderColor.A,
            blendModeInt);
    }

    public override void DrawString(string text, IFont font, int size, float x, float y,
        float fontScale, Color fontColor, bool worldPos = true,
        IGameRenderTexture? renderTexture = null, Color? borderColor = null)
    {
        if (string.IsNullOrEmpty(text) || font == null) return;

        var fontName = font.Name;
        var fontSize = (int)(size * fontScale);
        if (fontSize <= 0) return;

        var bc = borderColor ?? new Color(0, 0, 0, 0);

        // Render text to a temporary texture via Canvas2D
        var result = ((IJSInProcessRuntime)_js).Invoke<TextRenderResult?>(
            "IntersectWebGL.renderTextToTexture",
            text, fontName, fontSize,
            fontColor.R, fontColor.G, fontColor.B, fontColor.A,
            bc.R, bc.G, bc.B, bc.A);

        if (result == null) return;

        // Draw the text texture
        ((IJSInProcessRuntime)_js).InvokeVoid("IntersectWebGL.drawTexture",
            result.TextureId,
            0f, 0f, (float)result.Width, (float)result.Height,
            x, y, (float)result.Width, (float)result.Height,
            255, 255, 255, 255, 0);

        // Clean up temporary texture
        ((IJSInProcessRuntime)_js).InvokeVoid("IntersectWebGL.deleteTexture", result.TextureId);
    }

    public override void DrawString(string text, IFont font, int size, float x, float y,
        float fontScale, Color fontColor, bool worldPos,
        IGameRenderTexture renderTexture, FloatRect clipRect, Color? borderColor = null)
    {
        // Set scissor for clipping
        ((IJSInProcessRuntime)_js).InvokeVoid("IntersectWebGL.setScissor",
            (int)clipRect.X, (int)clipRect.Y, (int)clipRect.Width, (int)clipRect.Height);

        DrawString(text, font, size, x, y, fontScale, fontColor, worldPos, renderTexture, borderColor);

        ((IJSInProcessRuntime)_js).InvokeVoid("IntersectWebGL.clearScissor");
    }

    public override Vector2 MeasureText(string? text, IFont? font, int size, float fontScale)
    {
        if (string.IsNullOrEmpty(text) || font == null) return Vector2.Zero;

        var fontSize = (int)(size * fontScale);
        if (fontSize <= 0) return Vector2.Zero;

        var result = ((IJSInProcessRuntime)_js).Invoke<TextMeasureResult>(
            "IntersectWebGL.measureText", text, font.Name, fontSize);

        return new Vector2(result.Width, result.Height);
    }

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

    public override void DrawTileBuffer(GameTileBuffer buffer)
    {
        if (buffer is not WebTileBuffer webBuffer) return;
        if (buffer.Texture == null) return;

        var texId = (buffer.Texture as WebTexture)?.PlatformTextureId ?? 0;
        if (texId <= 0) return;

        ((IJSInProcessRuntime)_js).InvokeVoid("IntersectWebGL.drawBuffers",
            webBuffer.VertexBufferId, webBuffer.IndexBufferId, texId,
            webBuffer.IndexCount, 0);
    }

    public override void RequestScreenshot(string? pathToScreenshots = default)
    {
        // Screenshots in web could trigger a download
        Console.WriteLine("Screenshot not yet implemented for web client");
    }

    public IGameTexture LoadTexture(string name, string path)
    {
        return new WebTexture(_js, name, path);
    }

    public IGameTexture CreateTextureFromStreamFactory(string name, Func<Stream> factory)
    {
        return new WebTexture(_js, name, factory);
    }

    private record TextRenderResult(int TextureId, int Width, int Height);
    private record TextMeasureResult(int Width, int Height);
}
