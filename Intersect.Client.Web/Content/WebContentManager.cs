using Intersect.Client.Framework.Content;
using Intersect.Client.Framework.File_Management;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.Web.Graphics;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Content;

/// <summary>
/// Web content manager that loads assets via HTTP fetch.
/// </summary>
public class WebContentManager : GameContentManager
{
    private readonly IJSRuntime _js;
    private string _assetBaseUrl = "/resources";

    public WebContentManager(IJSRuntime js)
    {
        _js = js;
        Current = this;
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

    public override void LoadTexturePacks()
    {
        // Texture packs loaded on demand in web
    }

    public override void LoadTilesets(string[] tilesetnames)
    {
        foreach (var name in tilesetnames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var url = $"{_assetBaseUrl}/tilesets/{name}";
            var texture = new WebTexture(_js, name, url);
            _ = texture.LoadFromUrlAsync(url);
        }
    }

    public override void LoadItems()
    {
        // Items loaded on demand
    }

    public override void LoadEntities()
    {
        // Entities loaded on demand
    }

    public override void LoadSpells()
    {
        // Spells loaded on demand
    }

    public override void LoadAnimations()
    {
        // Animations loaded on demand
    }

    public override void LoadFaces()
    {
        // Faces loaded on demand
    }

    public override void LoadImages()
    {
        // Images loaded on demand
    }

    public override void LoadFogs()
    {
        // Fogs loaded on demand
    }

    public override void LoadResources()
    {
        // Resources loaded on demand
    }

    public override void LoadPaperdolls()
    {
        // Paperdolls loaded on demand
    }

    public override void LoadGui()
    {
        // GUI loaded on demand
    }

    public override void LoadFonts()
    {
        // Web fonts loaded via CSS FontFace API
        // Create default font objects
    }

    public override void LoadShaders()
    {
        // WebGL shaders are GLSL, not XNB
        // Load from JS-side
    }

    public WebTexture GetWebTexture(ContentType type, string name)
    {
        var path = GetContentPath(type);
        var url = $"{_assetBaseUrl}/{path}/{name}";
        var tex = new WebTexture(_js, name, url);
        _ = tex.LoadFromUrlAsync(url);
        return tex;
    }
}
