using Intersect.Client.Framework.Graphics;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Graphics;

/// <summary>
/// Web font implementation using Canvas2D text measurement.
/// </summary>
public class WebFont : IFont
{
    private readonly IJSRuntime _js;
    private readonly HashSet<int> _sizes;

    public WebFont(IJSRuntime js, string name, IEnumerable<int> sizes)
    {
        _js = js;
        Name = name;
        _sizes = new HashSet<int>(sizes);
        if (_sizes.Count == 0)
        {
            // Default sizes
            for (var i = 8; i <= 48; i += 2) _sizes.Add(i);
        }
    }

    public string Name { get; }

    public ICollection<int> SupportedSizes => _sizes;

    public int PickBestMatchFor(int size)
    {
        if (_sizes.Contains(size)) return size;
        return _sizes.OrderBy(s => Math.Abs(s - size)).FirstOrDefault();
    }

    public int GetNextFontSize(int startSize, int direction, int limit = 0)
    {
        var ordered = direction > 0
            ? _sizes.Where(s => s > startSize).OrderBy(s => s)
            : _sizes.Where(s => s < startSize).OrderByDescending(s => s);

        var next = ordered.FirstOrDefault();
        if (next == 0) return startSize;
        if (limit > 0 && Math.Abs(next - startSize) > limit) return startSize;
        return next;
    }
}
