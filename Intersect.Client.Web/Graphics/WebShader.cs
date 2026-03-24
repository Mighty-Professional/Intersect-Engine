using System.Numerics;
using Intersect.Client.Framework.Graphics;
using Intersect.Framework.Core;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Graphics;

/// <summary>
/// WebGL shader program wrapper.
/// </summary>
public class WebShader : GameShader
{
    private readonly IJSRuntime _js;
    private readonly string _name;
    private int _programId;
    private bool _valuesChanged;
    private IGameTexture? _texture;

    private readonly Dictionary<string, float> _floats = new();
    private readonly Dictionary<string, int> _ints = new();
    private readonly Dictionary<string, Color> _colors = new();
    private readonly Dictionary<string, Vector2> _vectors = new();

    public WebShader(IJSRuntime js, string name, int programId = 0)
    {
        _js = js;
        _name = name;
        _programId = programId;
    }

    public override IGameTexture? Texture
    {
        get => _texture;
        set => _texture = value;
    }

    public override void SetFloat(string key, float val)
    {
        _floats[key] = val;
        _valuesChanged = true;
    }

    public override void SetInt(string key, int val)
    {
        _ints[key] = val;
        _valuesChanged = true;
    }

    public override void SetColor(string key, Color val)
    {
        _colors[key] = val;
        _valuesChanged = true;
    }

    public override void SetVector2(string key, Vector2 val)
    {
        _vectors[key] = val;
        _valuesChanged = true;
    }

    public override bool ValuesChanged() => _valuesChanged;

    public override void ResetChanged() => _valuesChanged = false;

    public override object GetShader() => _programId;
}
