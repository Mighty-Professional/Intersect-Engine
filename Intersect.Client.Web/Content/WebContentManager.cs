using Intersect.Client.Core;
using Intersect.Client.Framework.Content;
using Intersect.Client.Framework.File_Management;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.Web.Graphics;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Content;

/// <summary>
/// Web content manager that loads assets on-demand via HTTP fetch.
/// Overrides GetTexture to lazily create and cache web textures.
/// </summary>
public class WebContentManager : GameContentManager
{
    private readonly IJSRuntime _js;
    private string _assetBaseUrl = "/resources";

    public WebContentManager(IJSRuntime js)
    {
        _js = js;
    }

    public string AssetBaseUrl
    {
        get => _assetBaseUrl;
        set => _assetBaseUrl = value.TrimEnd('/');
    }

    private string GetContentPath(ContentType type)
    {
        return type switch
        {
            ContentType.Animation => "animations",
            ContentType.Entity => "entities",
            ContentType.Face => "faces",
            ContentType.Fog => "fogs",
            ContentType.Image => "images",
            ContentType.Interface => "gui",
            ContentType.Item => "items",
            ContentType.Miscellaneous => "misc",
            ContentType.Paperdoll => "paperdolls",
            ContentType.Resource => "resources",
            ContentType.Spell => "spells",
            ContentType.Tileset => "tilesets",
            ContentType.Font => "fonts",
            ContentType.Music => "music",
            ContentType.Sound => "sounds",
            _ => "misc"
        };
    }

    private static string TextureTypeToContentPath(TextureType type)
    {
        return type switch
        {
            TextureType.Tileset => "tilesets",
            TextureType.Item => "items",
            TextureType.Entity => "entities",
            TextureType.Spell => "spells",
            TextureType.Animation => "animations",
            TextureType.Face => "faces",
            TextureType.Image => "images",
            TextureType.Fog => "fogs",
            TextureType.Resource => "resources",
            TextureType.Paperdoll => "paperdolls",
            TextureType.Gui => "gui",
            TextureType.Misc => "misc",
            _ => "misc"
        };
    }

    private IDictionary<string, IAsset> GetTextureDictForType(TextureType type)
    {
        return type switch
        {
            TextureType.Tileset => mTilesetDict,
            TextureType.Item => mItemDict,
            TextureType.Entity => mEntityDict,
            TextureType.Spell => mSpellDict,
            TextureType.Animation => mAnimationDict,
            TextureType.Face => mFaceDict,
            TextureType.Image => mImageDict,
            TextureType.Fog => mFogDict,
            TextureType.Resource => mResourceDict,
            TextureType.Paperdoll => mPaperdollDict,
            TextureType.Gui => mGuiDict,
            TextureType.Misc => mMiscDict,
            _ => mMiscDict
        };
    }

    /// <summary>
    /// Override GetTexture to load textures on-demand via HTTP.
    /// First checks the cache dictionary, then creates and caches a new WebTexture.
    /// </summary>
    public override IGameTexture? GetTexture(TextureType type, string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        var key = name.ToLower();
        var dict = GetTextureDictForType(type);

        // Return cached texture if available
        if (dict.TryGetValue(key, out var cached))
            return cached as IGameTexture;

        // Create and cache a new web texture
        var contentPath = TextureTypeToContentPath(type);
        var url = $"{_assetBaseUrl}/{contentPath}/{name}";
        var texture = new WebTexture(_js, name, url);
        dict[key] = texture;

        // Fire-and-forget async load
        _ = texture.LoadFromUrlAsync(url);

        return texture;
    }

    /// <summary>
    /// Implement abstract Load for on-demand web content loading.
    /// </summary>
    protected override TAsset Load<TAsset>(
        Dictionary<string, IAsset> lookup,
        ContentType contentType,
        string assetName,
        Func<Stream> createStream)
    {
        // For texture types, create a WebTexture and cache it
        if (typeof(IGameTexture).IsAssignableFrom(typeof(TAsset)))
        {
            var path = GetContentPath(contentType);
            var url = $"{_assetBaseUrl}/{path}/{assetName}";
            var texture = new WebTexture(_js, assetName, url);
            lookup[assetName] = texture;
            _ = texture.LoadFromUrlAsync(url);
            return (texture as TAsset)!;
        }

        // For non-texture types (audio, etc.), create from stream factory
        var asset = Core.Graphics.Renderer.CreateTextureFromStreamFactory(assetName, createStream);
        if (asset is TAsset typedAsset)
        {
            lookup[assetName] = typedAsset;
            return typedAsset;
        }

        throw new InvalidOperationException($"Cannot load asset type {typeof(TAsset).Name} for content type {contentType}");
    }

    public override void LoadTexturePacks()
    {
        // Texture packs loaded on demand in web
    }

    public override void LoadTilesets(string[] tilesetnames)
    {
        foreach (var name in tilesetnames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var key = name.ToLower();
            if (mTilesetDict.ContainsKey(key)) continue;

            var url = $"{_assetBaseUrl}/tilesets/{name}";
            var texture = new WebTexture(_js, name, url);
            mTilesetDict[key] = texture;
            _ = texture.LoadFromUrlAsync(url);
        }

        TilesetsLoaded = true;
    }

    public override void LoadItems() { }
    public override void LoadEntities() { }
    public override void LoadSpells() { }
    public override void LoadAnimations() { }
    public override void LoadFaces() { }
    public override void LoadImages() { }
    public override void LoadFogs() { }
    public override void LoadResources() { }
    public override void LoadPaperdolls() { }
    public override void LoadGui() { }
    public override void LoadMisc() { }

    public override void LoadFonts()
    {
        // Map of engine font names → web font file names
        // The engine expects specific font names (e.g. "sourcesansproblack")
        // but we serve them all from the same Source Sans Pro variable font
        var fontMappings = new Dictionary<string, string>
        {
            { "sourcesanspro-regular", "sourcesanspro-regular" },
            { "sourcesanspro-bold", "sourcesanspro-bold" },
            { "sourcesanspro-italic", "sourcesanspro-italic" },
            { "sourcesansproblack", "sourcesanspro-bold" },  // Black weight → use bold
            { "sourcesanspro", "sourcesanspro-regular" },    // Base name → regular
        };

        foreach (var (fontName, fileName) in fontMappings)
        {
            // Load font file from server via CSS FontFace API
            var url = $"{_assetBaseUrl}/fonts/{fileName}.ttf";
            _ = LoadWebFontAsync(fontName, url);

            // Register in font dictionary so GetFont() works immediately
            // Canvas2D will use system fallback until the web font loads
            var font = Core.Graphics.Renderer.LoadFont(fontName, new Dictionary<int, FileInfo>());
            mFontDict.TryAdd(fontName, font);
        }
    }

    private async Task LoadWebFontAsync(string fontName, string url)
    {
        try
        {
            await ((IJSRuntime)_js).InvokeAsync<bool>("IntersectWebGL.loadFont", fontName, url);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load web font '{fontName}': {ex.Message}");
        }
    }

    public override void LoadShaders()
    {
        // WebGL shaders are GLSL, loaded from JS side
    }

    /// <summary>
    /// Override layout loading for web — fetch JSON via HTTP instead of filesystem.
    /// </summary>
    protected override string GetLayout(UI stage, string name, string resolution, bool skipCache, out bool cacheHit)
    {
        cacheHit = false;
        var key = new KeyValuePair<UI, string>(stage, $"{name}.{resolution}.json");
        if (!skipCache && mUiDict.TryGetValue(key, out var rawLayout))
        {
            cacheHit = true;
            return rawLayout;
        }

        var stageName = stage.ToString().ToLowerInvariant();
        if (stage == UI.InGame)
        {
            stageName = "game";
        }

        // Try resolution-specific layout first, then generic
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(resolution))
        {
            paths.Add($"{_assetBaseUrl}/gui/layouts/{stageName}/{name}.{resolution}.json");
        }
        paths.Add($"{_assetBaseUrl}/gui/layouts/{stageName}/{name}.json");

        foreach (var url in paths)
        {
            try
            {
                var jsSync = _js as Microsoft.JSInterop.IJSInProcessRuntime;
                if (jsSync == null) continue;

                var json = jsSync.Invoke<string?>("IntersectWebContent.fetchTextSync", url);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    mUiDict[key] = json;
                    return json;
                }
            }
            catch
            {
                // URL not found or fetch failed, try next
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Override to prevent filesystem writes in web context.
    /// </summary>
    public override void SaveUIJson(UI stage, string name, string json, string? resolution)
    {
        // No-op in web — can't write to server filesystem
    }

    /// <summary>
    /// Override to use HTTP-based layout loading (skip filesystem watchers).
    /// </summary>
    public override bool GetLayout(UI stage, string name, string resolution, bool skipCache, Action<string, bool> layoutHandler)
    {
        var result = GetLayout(stage, name, resolution, skipCache, out var cacheHit);
        layoutHandler(result, cacheHit);
        return true;
    }

    public override void LoadSounds() { }
    public override void LoadMusic() { }
}
