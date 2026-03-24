using Intersect.Client.Framework.Graphics;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Graphics;

/// <summary>
/// WebGL vertex buffer wrapper.
/// </summary>
public class WebVertexBuffer : IVertexBuffer, IDisposable
{
    private readonly IJSRuntime _js;
    private int _bufferId;
    private readonly int _count;
    private readonly Type _vertexType;
    private readonly int _vertexSizeBytes;
    private bool _disposed;

    // WebGL ARRAY_BUFFER = 34962
    private const int GL_ARRAY_BUFFER = 34962;

    public int PlatformBufferId => _bufferId;

    public WebVertexBuffer(IJSRuntime js, int count, Type vertexType, bool dynamic)
    {
        _js = js;
        _count = count;
        _vertexType = vertexType;
        _vertexSizeBytes = 32; // 8 floats * 4 bytes (position, texcoord, color)

        _bufferId = ((IJSInProcessRuntime)js).Invoke<int>(
            "IntersectWebGL.createBuffer", GL_ARRAY_BUFFER, count * _vertexSizeBytes, dynamic);
    }

    public uint Id => (uint)_bufferId;
    public int Count => _count;
    public int SizeBytes => _count * _vertexSizeBytes;
    public Type VertexType => _vertexType;
    public PrimitiveType PrimitiveType { get; set; } = PrimitiveType.TriangleList;

    public void SetVertexData(float[] data)
    {
        // This would use JS interop to upload the data
        // For now, data is set when creating tile buffers
    }

    public bool GetData<TVertex>(TVertex[] destination) where TVertex : struct => false;
    public bool GetData<TVertex>(TVertex[] destination, int destinationOffset, int length) where TVertex : struct => false;
    public bool GetData<TVertex>(int bufferOffset, TVertex[] destination, int destinationOffset, int length) where TVertex : struct => false;
    public bool SetData<TVertex>(TVertex[] data) where TVertex : struct => true;
    public bool SetData<TVertex>(TVertex[] data, int sourceOffset, int length) where TVertex : struct => true;
    public bool SetData<TVertex>(int destinationOffset, TVertex[] data, int sourceOffset, int length) where TVertex : struct => true;
    public bool SetData<TVertex>(int destinationOffset, TVertex[] data, int sourceOffset, int length, BufferWriteMode bufferWriteMode) where TVertex : struct => true;

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
