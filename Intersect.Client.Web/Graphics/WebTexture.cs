using System.Numerics;
using Intersect.Client.Framework.Content;
using Intersect.Client.Framework.GenericClasses;
using Intersect.Client.Framework.Graphics;
using Intersect.Framework.Core;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Graphics;

/// <summary>
/// WebGL texture implementation backed by a JS-side texture handle.
/// </summary>
public class WebTexture : IGameTexture
{
    private readonly IJSInProcessRuntime? _jsSync;
    private readonly IJSRuntime _js;
    private readonly string? _url;
    private readonly Func<Stream>? _streamFactory;
    private int _platformTextureId;
    private int _width;
    private int _height;
    private bool _loaded;
    private bool _disposed;

    public int PlatformTextureId => _platformTextureId;

    public WebTexture(IJSRuntime js, string name, string url)
    {
        _js = js;
        _jsSync = js as IJSInProcessRuntime;
        Name = name;
        _url = url;
    }

    public WebTexture(IJSRuntime js, string name, Func<Stream> streamFactory)
    {
        _js = js;
        _jsSync = js as IJSInProcessRuntime;
        Name = name;
        _streamFactory = streamFactory;
    }

    public WebTexture(IJSRuntime js, string name, int platformTextureId, int width, int height)
    {
        _js = js;
        _jsSync = js as IJSInProcessRuntime;
        Name = name;
        _platformTextureId = platformTextureId;
        _width = width;
        _height = height;
        _loaded = true;
    }

    /// <summary>Parameterless constructor for name-only textures (atlas references).</summary>
    public WebTexture(IJSRuntime js, string name)
    {
        _js = js;
        _jsSync = js as IJSInProcessRuntime;
        Name = name;
    }

    public string Name { get; set; }
    public string Id => Name;
    public long AccessTime { get; private set; } = Environment.TickCount64;
    public bool IsMissingOrCorrupt => false;
    public bool IsPinned => false;
    public int Area => _width * _height;
    public Vector2 Dimensions => new(_width, _height);
    public FloatRect Bounds => new(0, 0, _width, _height);
    public Vector2 Center => new(_width / 2f, _height / 2f);
    public AtlasReference? AtlasReference { get; set; }
    public int Width => _width;
    public int Height => _height;
    public bool IsDisposed => _disposed;
    public bool IsLoaded => _loaded;

    public Color this[int x, int y] => GetPixel(x, y);
    public Color this[Intersect.Point point] => GetPixel(point.X, point.Y);

    public event Action<IAsset>? Disposed;
    public event Action<IAsset>? Loaded;
    public event Action<IAsset>? Unloaded;

    public int CompareTo(IGameTexture? other)
    {
        if (other == null) return 1;
        return string.Compare(Name, other.ToString(), StringComparison.Ordinal);
    }

    public bool Unload()
    {
        if (_platformTextureId > 0)
        {
            _jsSync?.InvokeVoid("IntersectWebGL.deleteTexture", _platformTextureId);
            _platformTextureId = 0;
        }
        _loaded = false;
        Unloaded?.Invoke(this);
        return true;
    }

    public object? GetTexture() => _platformTextureId;

    public TPlatformTexture? GetTexture<TPlatformTexture>() where TPlatformTexture : class
        => _platformTextureId as object as TPlatformTexture;

    public void Reload()
    {
        if (!string.IsNullOrEmpty(_url))
        {
            _ = LoadFromUrlAsync(_url);
        }
    }

    public Color GetPixel(int x, int y) => Color.Transparent;

    internal async Task LoadFromUrlAsync(string url)
    {
        try
        {
            var result = await _js.InvokeAsync<TextureLoadResult>(
                "IntersectWebGL.createTextureFromUrl", url);
            _platformTextureId = result.Id;
            _width = result.Width;
            _height = result.Height;
            _loaded = true;
            Loaded?.Invoke(this);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load texture '{Name}' from {url}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unload();
        Disposed?.Invoke(this);
    }

    public override string ToString() => $"WebTexture({Name}, {_width}x{_height})";

    private record TextureLoadResult(int Id, int Width, int Height);
}
