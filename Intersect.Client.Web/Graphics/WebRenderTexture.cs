using System.Numerics;
using Intersect.Client.Framework.Content;
using Intersect.Client.Framework.GenericClasses;
using Intersect.Client.Framework.Graphics;
using Intersect.Framework.Core;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Graphics;

/// <summary>
/// WebGL framebuffer-backed render texture.
/// </summary>
public class WebRenderTexture : IGameRenderTexture
{
    private readonly IJSInProcessRuntime _js;
    private int _framebufferId;
    private int _textureId;
    private readonly int _width;
    private readonly int _height;
    private bool _disposed;

    public WebRenderTexture(IJSInProcessRuntime js, int width, int height)
    {
        _js = js;
        _width = width;
        _height = height;

        var result = js.Invoke<FbCreateResult>("IntersectWebGL.createFramebuffer", width, height);
        _framebufferId = result.FbId;
        _textureId = result.TextureId;
    }

    public string Name { get; set; } = "RenderTarget";
    public string Id => Name;
    public long AccessTime { get; private set; } = Environment.TickCount64;
    public bool IsMissingOrCorrupt => false;
    public bool IsPinned => true;
    public int Area => _width * _height;
    public Vector2 Dimensions => new(_width, _height);
    public FloatRect Bounds => new(0, 0, _width, _height);
    public Vector2 Center => new(_width / 2f, _height / 2f);
    public AtlasReference? AtlasReference => null;
    public int Width => _width;
    public int Height => _height;
    public bool IsDisposed => _disposed;
    public bool IsLoaded => _textureId > 0;

    public Color this[int x, int y] => Color.Transparent;
    public Color this[System.Drawing.Point point] => Color.Transparent;

    public event Action<IAsset>? Disposed;
    public event Action<IAsset>? Loaded;
    public event Action<IAsset>? Unloaded;

    public int CompareTo(IGameTexture? other)
    {
        if (other == null) return 1;
        return string.Compare(Name, other.ToString(), StringComparison.Ordinal);
    }

    public bool Begin()
    {
        _js.InvokeVoid("IntersectWebGL.bindFramebuffer", _framebufferId);
        return true;
    }

    public void Clear(Color color)
    {
        _js.InvokeVoid("IntersectWebGL.clear",
            color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }

    public void End()
    {
        _js.InvokeVoid("IntersectWebGL.bindFramebuffer", 0);
    }

    public bool Unload()
    {
        if (_framebufferId > 0)
        {
            _js.InvokeVoid("IntersectWebGL.deleteFramebuffer", _framebufferId);
            _framebufferId = 0;
            _textureId = 0;
        }
        Unloaded?.Invoke(this);
        return true;
    }

    public object? GetTexture() => _textureId;
    public TPlatformTexture? GetTexture<TPlatformTexture>() where TPlatformTexture : class
        => _textureId as object as TPlatformTexture;
    public void Reload() { }
    public Color GetPixel(int x, int y) => Color.Transparent;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unload();
        Disposed?.Invoke(this);
    }

    private record FbCreateResult(int FbId, int TextureId);
}
