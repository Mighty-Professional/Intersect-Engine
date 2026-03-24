using Intersect.Client.Framework.Database;
using Intersect.Client.Framework.Graphics;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Database;

/// <summary>
/// Web database using localStorage for client preferences.
/// </summary>
public class WebDatabase : GameDatabase
{
    private readonly IJSRuntime _js;

    public WebDatabase(IJSRuntime js)
    {
        _js = js;
    }

    public override bool HasPreference(string key)
    {
        var val = ((IJSInProcessRuntime)_js).Invoke<string?>("IntersectStorage.getItem", key);
        return val != null;
    }

    public override void SavePreference(string key, string value)
    {
        ((IJSInProcessRuntime)_js).InvokeVoid("IntersectStorage.setItem", key, value);
    }

    public override string LoadPreference(string key)
    {
        return ((IJSInProcessRuntime)_js).Invoke<string?>("IntersectStorage.getItem", key) ?? string.Empty;
    }

    public override void DeletePreference(string key)
    {
        ((IJSInProcessRuntime)_js).InvokeVoid("IntersectStorage.removeItem", key);
    }
}
