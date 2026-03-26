using Intersect.Client.Framework.Input;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Input;

/// <summary>
/// Web clipboard using navigator.clipboard API.
/// </summary>
public class WebClipboard : GameClipboard
{
    private readonly IJSInProcessRuntime _js;
    private string _text = string.Empty;

    public WebClipboard(IJSInProcessRuntime js)
    {
        _js = js;
    }

    public override bool IsEmpty => string.IsNullOrEmpty(_text);

    public override bool IsEnabled => true;

    public override void SetText(string data)
    {
        _text = data;
        // Fire-and-forget clipboard write; clipboard API may fail if page doesn't have focus
        _ = SetClipboardAsync(data);
    }

    private async Task SetClipboardAsync(string data)
    {
        try
        {
            await ((IJSRuntime)_js).InvokeVoidAsync("navigator.clipboard.writeText", data);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Clipboard write failed: {ex.Message}");
        }
    }

    public override string GetText() => _text;
}
