using Intersect.Client.Framework.Graphics;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Graphics;

/// <summary>
/// WebGL index buffer wrapper.
/// </summary>
public class WebIndexBuffer : IIndexBuffer, IDisposable
{
    private readonly IJSRuntime _js;
    private int _bufferId;
    private readonly int _count;
    private readonly Type _indexType;
    private readonly int _indexSizeBytes;
    private bool _disposed;

    // WebGL ELEMENT_ARRAY_BUFFER = 34963
    private const int GL_ELEMENT_ARRAY_BUFFER = 34963;

    public int PlatformBufferId => _bufferId;

    public WebIndexBuffer(IJSRuntime js, int count, Type indexType, bool dynamic)
    {
        _js = js;
        _count = count;
        _indexType = indexType;
        _indexSizeBytes = indexType == typeof(int) || indexType == typeof(uint) ? 4 : 2;

        _bufferId = ((IJSInProcessRuntime)js).Invoke<int>(
            "IntersectWebGL.createBuffer", GL_ELEMENT_ARRAY_BUFFER, count * _indexSizeBytes, dynamic);
    }

    public uint Id => (uint)_bufferId;
    public int Count => _count;
    public int SizeBytes => _count * _indexSizeBytes;
    public Type IndexType => _indexType;

    public void SetIndexData(ushort[] data)
    {
        // Upload index data via JS interop
    }

    public bool GetData<TIndex>(TIndex[] destination) where TIndex : struct => false;
    public bool GetData<TIndex>(TIndex[] destination, int destinationOffset, int length) where TIndex : struct => false;
    public bool GetData<TIndex>(int bufferOffset, TIndex[] destination, int destinationOffset, int length) where TIndex : struct => false;
    public bool SetData<TIndex>(TIndex[] data) where TIndex : struct => true;
    public bool SetData<TIndex>(TIndex[] data, int sourceOffset, int length) where TIndex : struct => true;
    public bool SetData<TIndex>(int destinationOffset, TIndex[] data, int sourceOffset, int length) where TIndex : struct => true;
    public bool SetData<TIndex>(int destinationOffset, TIndex[] data, int sourceOffset, int length, BufferWriteMode bufferWriteMode) where TIndex : struct => true;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_bufferId > 0)
        {
            ((IJSInProcessRuntime)_js).InvokeVoid("IntersectWebGL.deleteBuffer", _bufferId);
            _bufferId = 0;
        }
    }
}
