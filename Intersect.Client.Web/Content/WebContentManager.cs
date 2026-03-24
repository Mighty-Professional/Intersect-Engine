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

        // Create and cache a new web texture (loads asynchronously)
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
        // Register default fonts that the engine expects
        // The actual font files will be loaded from the server via CSS FontFace API
        var defaultFonts = new[] { "sourcesanspro-regular", "sourcesanspro-bold", "sourcesanspro-italic" };
        var defaultExtensions = new[] { ".ttf", ".otf", ".woff", ".woff2" };

        foreach (var fontName in defaultFonts)
        {
            // Try to load font file from server
            foreach (var ext in defaultExtensions)
            {
                var url = $"{_assetBaseUrl}/fonts/{fontName}{ext}";
                _ = LoadWebFontAsync(fontName, url);
                break; // Try first extension, async will handle failure
            }

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

    public override void LoadSounds() { }
    public override void LoadMusic() { }
}
