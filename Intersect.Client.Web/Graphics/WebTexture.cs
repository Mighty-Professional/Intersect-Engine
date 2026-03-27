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
    private bool _loadInProgress;

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

    public object? GetTexture() => _platformTextureId > 0 ? _platformTextureId : null;

    public TPlatformTexture? GetTexture<TPlatformTexture>() where TPlatformTexture : class
        => _platformTextureId > 0 ? _platformTextureId as object as TPlatformTexture : null;

    public void Reload()
    {
        if (_loaded && _platformTextureId > 0)
        {
            // Already loaded (e.g. via sync load), fire event immediately
            // so subscribers like TexturedBase.OnTextureLoaded run synchronously
            Loaded?.Invoke(this);
            return;
        }

        // Don't start a second async load if one is already in progress
        if (_loadInProgress)
        {
            return;
        }

        if (!string.IsNullOrEmpty(_url))
        {
            _ = LoadFromUrlAsync(_url);
        }
    }

    public Color GetPixel(int x, int y)
    {
        if (_platformTextureId <= 0 || _jsSync == null) return Color.Transparent;
        try
        {
            var result = _jsSync.Invoke<PixelResult?>("IntersectWebGL.readPixel", _platformTextureId, x, y);
            if (result != null)
            {
                return new Color(result.A, result.R, result.G, result.B);
            }
        }
        catch
        {
            // Fall back to transparent if pixel reading fails
        }
        return Color.Transparent;
    }

    private record PixelResult(int R, int G, int B, int A);

    internal async Task LoadFromUrlAsync(string url)
    {
        if (_loadInProgress) return;
        _loadInProgress = true;
        WebDebugLog.Log("TEXTURE", $"LoadAsync start: {Name} url={url}");
        try
        {
            var result = await _js.InvokeAsync<TextureLoadResult>(
                "IntersectWebGL.createTextureFromUrl", url);
            _platformTextureId = result.Id;
            _width = result.Width;
            _height = result.Height;
            _loaded = true;
            WebDebugLog.Log("TEXTURE", $"LoadAsync OK: {Name} id={_platformTextureId} {_width}x{_height} subscribers={Loaded?.GetInvocationList()?.Length ?? 0}");
            try
            {
                Loaded?.Invoke(this);
            }
            catch (Exception loadedEx)
            {
                WebDebugLog.Warn("TEXTURE", $"LoadAsync Loaded event EXCEPTION for {Name}: {loadedEx}");
            }
        }
        catch (JSException ex)
        {
            WebDebugLog.Log("TEXTURE", $"LoadAsync 404/fail: {Name} ({ex.Message})");
        }
        catch (Exception ex)
        {
            WebDebugLog.Warn("TEXTURE", $"LoadAsync error: {Name} {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _loadInProgress = false;
        }
    }

    /// <summary>
    /// Load texture synchronously using XHR. Used for critical textures like UI skin.
    /// </summary>
    internal void LoadFromUrlSync(string url)
    {
        if (_jsSync == null) return;
        WebDebugLog.Log("TEXTURE", $"LoadSync start: {Name} url={url}");
        try
        {
            var result = _jsSync.Invoke<TextureLoadResult?>(
                "IntersectWebGL.createTextureFromUrlSync", url);
            if (result != null)
            {
                _platformTextureId = result.Id;
                _width = result.Width;
                _height = result.Height;
                _loaded = true;
                WebDebugLog.Log("TEXTURE", $"LoadSync OK: {Name} id={_platformTextureId} {_width}x{_height}");
                Loaded?.Invoke(this);
            }
            else
            {
                WebDebugLog.Warn("TEXTURE", $"LoadSync returned null: {Name} url={url}");
                _ = LoadFromUrlAsync(url);
            }
        }
        catch (Exception ex)
        {
            WebDebugLog.Warn("TEXTURE", $"LoadSync failed: {Name} {ex.Message}, falling back to async");
            _ = LoadFromUrlAsync(url);
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
