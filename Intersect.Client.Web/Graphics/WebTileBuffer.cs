using Intersect.Client.Framework.Graphics;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Graphics;

/// <summary>
/// WebGL tile buffer for batched tile rendering.
/// </summary>
public class WebTileBuffer : GameTileBuffer
{
    private readonly IJSRuntime _js;
    private WebVertexBuffer? _vertexBuffer;
    private WebIndexBuffer? _indexBuffer;
    private IGameTexture? _texture;
    private readonly List<float> _vertices = new();
    private readonly List<ushort> _indices = new();
    private int _tileCount;

    public WebTileBuffer(IJSRuntime js)
    {
        _js = js;
        TileBufferCount++;
    }

    public override IIndexBuffer IndexBuffer => _indexBuffer!;
    public override IVertexBuffer VertexBuffer => _vertexBuffer!;
    public override IGameTexture? Texture => _texture;
    public override bool Supported => true;

    public int VertexBufferId => (_vertexBuffer as WebVertexBuffer)?.PlatformBufferId ?? 0;
    public int IndexBufferId => (_indexBuffer as WebIndexBuffer)?.PlatformBufferId ?? 0;
    public int IndexCount => _indices.Count;

    public override bool TryAddTile(IGameTexture texture, int x, int y, int srcX, int srcY, int srcW, int srcH)
    {
        _texture = texture;

        var tw = texture.Width > 0 ? (float)texture.Width : 1f;
        var th = texture.Height > 0 ? (float)texture.Height : 1f;

        var u0 = srcX / tw;
        var v0 = srcY / th;
        var u1 = (srcX + srcW) / tw;
        var v1 = (srcY + srcH) / th;

        var vi = (ushort)(_tileCount * 4);

        // Vertices: x, y, u, v, r, g, b, a
        AddVertex(x, y, u0, v0);
        AddVertex(x + srcW, y, u1, v0);
        AddVertex(x + srcW, y + srcH, u1, v1);
        AddVertex(x, y + srcH, u0, v1);

        // Indices
        _indices.Add(vi);
        _indices.Add((ushort)(vi + 1));
        _indices.Add((ushort)(vi + 2));
        _indices.Add(vi);
        _indices.Add((ushort)(vi + 2));
        _indices.Add((ushort)(vi + 3));

        _tileCount++;
        return true;
    }

    public override bool TryUpdateTile(IGameTexture texture, int x, int y, int srcX, int srcY, int srcW, int srcH)
    {
        // For simplicity, just rebuild
        return TryAddTile(texture, x, y, srcX, srcY, srcW, srcH);
    }

    private void AddVertex(float x, float y, float u, float v)
    {
        _vertices.Add(x);
        _vertices.Add(y);
        _vertices.Add(u);
        _vertices.Add(v);
        _vertices.Add(1f); // r
        _vertices.Add(1f); // g
        _vertices.Add(1f); // b
        _vertices.Add(1f); // a
    }

    public override bool SetData()
    {
        if (_vertices.Count == 0) return false;

        // Create GPU buffers
        _vertexBuffer = new WebVertexBuffer(_js, _vertices.Count / 8, typeof(float), true);
        _indexBuffer = new WebIndexBuffer(_js, _indices.Count, typeof(ushort), true);

        // Upload data
        _vertexBuffer.SetVertexData(_vertices.ToArray());
        _indexBuffer.SetIndexData(_indices.ToArray());

        return true;
    }

    public override void Dispose()
    {
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        TileBufferCount--;
    }
}
