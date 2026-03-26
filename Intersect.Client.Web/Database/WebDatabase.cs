using Intersect.Client.Framework.Database;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Database;

/// <summary>
/// Web database using localStorage for client preferences.
/// </summary>
public class WebDatabase : GameDatabase
{
    private readonly IJSInProcessRuntime _js;

    public WebDatabase(IJSInProcessRuntime js)
    {
        _js = js;
    }

    public override bool HasPreference(string key)
    {
        var val = _js.Invoke<string?>("IntersectStorage.getItem", key);
        return val != null;
    }

    public override void SavePreference<TValue>(string key, TValue value)
    {
        var stringValue = Convert.ToString(value) ?? string.Empty;
        _js.InvokeVoid("IntersectStorage.setItem", key, stringValue);
    }

    public override string LoadPreference(string key)
    {
        return _js.Invoke<string?>("IntersectStorage.getItem", key) ?? string.Empty;
    }

    public override void DeletePreference(string key)
    {
        _js.InvokeVoid("IntersectStorage.removeItem", key);
    }

    public override bool LoadConfig()
    {
        // Web client loads config from server or defaults
        return true;
    }
}
