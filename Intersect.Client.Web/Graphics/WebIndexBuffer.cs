using Intersect.Client.Framework.Graphics;
using Microsoft.JSInterop;

namespace Intersect.Client.Web.Graphics;

/// <summary>
/// WebGL index buffer wrapper.
/// </summary>
public class WebIndexBuffer : IIndexBuffer, IDisposable
{
    private readonly IJSInProcessRuntime _js;
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
        _js = (IJSInProcessRuntime)js;
        _count = count;
        _indexType = indexType;
        _indexSizeBytes = indexType == typeof(int) || indexType == typeof(uint) ? 4 : 2;

        _bufferId = _js.Invoke<int>(
            "IntersectWebGL.createBuffer", GL_ELEMENT_ARRAY_BUFFER, count * _indexSizeBytes, dynamic);
    }

    public uint Id => (uint)_bufferId;
    public int Count => _count;
    public int SizeBytes => _count * _indexSizeBytes;
    public Type IndexType => _indexType;

    public void SetIndexData(ushort[] data)
    {
        if (_bufferId <= 0 || data.Length == 0) return;
        _js.InvokeVoid("IntersectWebGL.setBufferDataUint16", _bufferId, data, 0);
    }

    public void SetIndexData(uint[] data)
    {
        if (_bufferId <= 0 || data.Length == 0) return;
        _js.InvokeVoid("IntersectWebGL.setBufferDataUint32", _bufferId, data, 0);
    }

    public bool GetData<TIndex>(TIndex[] destination) where TIndex : struct => false;
    public bool GetData<TIndex>(TIndex[] destination, int destinationOffset, int length) where TIndex : struct => false;
    public bool GetData<TIndex>(int bufferOffset, TIndex[] destination, int destinationOffset, int length) where TIndex : struct => false;

    public bool SetData<TIndex>(TIndex[] data) where TIndex : struct
    {
        return SetData(0, data, 0, data.Length);
    }

    public bool SetData<TIndex>(TIndex[] data, int sourceOffset, int length) where TIndex : struct
    {
        return SetData(0, data, sourceOffset, length);
    }

    public bool SetData<TIndex>(int destinationOffset, TIndex[] data, int sourceOffset, int length) where TIndex : struct
    {
        return SetData(destinationOffset, data, sourceOffset, length, BufferWriteMode.Overwrite);
    }

    public bool SetData<TIndex>(int destinationOffset, TIndex[] data, int sourceOffset, int length, BufferWriteMode bufferWriteMode) where TIndex : struct
    {
        if (_bufferId <= 0 || data.Length == 0 || length == 0) return false;

        var byteOffset = destinationOffset * _indexSizeBytes;

        if (typeof(TIndex) == typeof(ushort))
        {
            var ushortData = (ushort[])(object)data;
            if (sourceOffset == 0 && length == data.Length)
            {
                _js.InvokeVoid("IntersectWebGL.setBufferDataUint16", _bufferId, ushortData, byteOffset);
            }
            else
            {
                var slice = new ushort[length];
                Array.Copy(ushortData, sourceOffset, slice, 0, length);
                _js.InvokeVoid("IntersectWebGL.setBufferDataUint16", _bufferId, slice, byteOffset);
            }
            return true;
        }

        if (typeof(TIndex) == typeof(uint) || typeof(TIndex) == typeof(int))
        {
            // Convert int[] to uint[] if needed, or pass uint[] directly
            uint[] uintData;
            if (typeof(TIndex) == typeof(uint))
            {
                uintData = (uint[])(object)data;
            }
            else
            {
                var intData = (int[])(object)data;
                uintData = new uint[intData.Length];
                for (var i = 0; i < intData.Length; i++) uintData[i] = (uint)intData[i];
            }

            if (sourceOffset == 0 && length == data.Length)
            {
                _js.InvokeVoid("IntersectWebGL.setBufferDataUint32", _bufferId, uintData, byteOffset);
            }
            else
            {
                var slice = new uint[length];
                Array.Copy(uintData, sourceOffset, slice, 0, length);
                _js.InvokeVoid("IntersectWebGL.setBufferDataUint32", _bufferId, slice, byteOffset);
            }
            return true;
        }

        return false;
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
