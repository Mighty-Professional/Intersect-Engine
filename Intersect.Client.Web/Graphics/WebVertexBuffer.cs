using System.Runtime.InteropServices;
using Intersect.Client.Framework.Graphics;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Graphics;

/// <summary>
/// WebGL vertex buffer wrapper.
/// </summary>
public class WebVertexBuffer : IVertexBuffer, IDisposable
{
    private readonly IJSInProcessRuntime _js;
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
        _js = (IJSInProcessRuntime)js;
        _count = count;
        _vertexType = vertexType;
        _vertexSizeBytes = 32; // 8 floats * 4 bytes (position, texcoord, color)

        _bufferId = _js.Invoke<int>(
            "IntersectWebGL.createBuffer", GL_ARRAY_BUFFER, count * _vertexSizeBytes, dynamic);
    }

    public uint Id => (uint)_bufferId;
    public int Count => _count;
    public int SizeBytes => _count * _vertexSizeBytes;
    public Type VertexType => _vertexType;
    public PrimitiveType PrimitiveType { get; set; } = PrimitiveType.TriangleList;

    public void SetVertexData(float[] data)
    {
        if (_bufferId <= 0 || data.Length == 0) return;
        _js.InvokeVoid("IntersectWebGL.setBufferData", _bufferId, data, 0);
    }

    public bool GetData<TVertex>(TVertex[] destination) where TVertex : struct => false;
    public bool GetData<TVertex>(TVertex[] destination, int destinationOffset, int length) where TVertex : struct => false;
    public bool GetData<TVertex>(int bufferOffset, TVertex[] destination, int destinationOffset, int length) where TVertex : struct => false;

    public bool SetData<TVertex>(TVertex[] data) where TVertex : struct
    {
        return SetData(0, data, 0, data.Length);
    }

    public bool SetData<TVertex>(TVertex[] data, int sourceOffset, int length) where TVertex : struct
    {
        return SetData(0, data, sourceOffset, length);
    }

    public bool SetData<TVertex>(int destinationOffset, TVertex[] data, int sourceOffset, int length) where TVertex : struct
    {
        return SetData(destinationOffset, data, sourceOffset, length, BufferWriteMode.Overwrite);
    }

    public bool SetData<TVertex>(int destinationOffset, TVertex[] data, int sourceOffset, int length, BufferWriteMode bufferWriteMode) where TVertex : struct
    {
        if (_bufferId <= 0 || data.Length == 0 || length == 0) return false;

        var floatData = ConvertToFloats(data, sourceOffset, length);
        if (floatData == null) return false;

        var byteOffset = destinationOffset * _vertexSizeBytes;
        _js.InvokeVoid("IntersectWebGL.setBufferData", _bufferId, floatData, byteOffset);
        return true;
    }

    private static float[]? ConvertToFloats<T>(T[] data, int offset, int length) where T : struct
    {
        if (typeof(T) == typeof(float))
        {
            if (offset == 0 && length == data.Length)
                return (float[])(object)data;

            var slice = new float[length];
            Array.Copy(data, offset, slice, 0, length);
            return slice;
        }

        // Convert arbitrary struct to float array via raw bytes
        var structSize = Marshal.SizeOf<T>();
        var byteCount = length * structSize;
        var bytes = new byte[byteCount];
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            Marshal.Copy(handle.AddrOfPinnedObject() + offset * structSize, bytes, 0, byteCount);
        }
        finally
        {
            handle.Free();
        }

        var floats = new float[byteCount / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, byteCount);
        return floats;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_bufferId > 0)
        {
            _js.InvokeVoid("IntersectWebGL.deleteBuffer", _bufferId);
            _bufferId = 0;
        }
    }
}
