using Intersect.Client.Framework.Input;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Input;

/// <summary>
/// Web clipboard using navigator.clipboard API.
/// </summary>
public class WebClipboard : GameClipboard
{
    private readonly IJSRuntime _js;
    private string _text = string.Empty;

    public WebClipboard(IJSRuntime js)
    {
        _js = js;
    }

    public override bool IsEmpty => string.IsNullOrEmpty(_text);

    public override bool IsEnabled => true;

    public override void SetText(string data)
    {
        _text = data;
        _ = _js.InvokeVoidAsync("navigator.clipboard.writeText", data);
    }

    public override string GetText()
    {
        return _text;
    }
}
