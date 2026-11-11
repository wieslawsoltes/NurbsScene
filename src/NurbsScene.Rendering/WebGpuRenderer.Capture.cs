using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using GpuBuffer = Silk.NET.WebGPU.Buffer;

namespace NurbsScene.Rendering;

public sealed unsafe partial class WebGpuRenderer
{
    /// <summary>
    /// Capture the last completed presentation without reading Uno's borrowed target.
    /// An extra GPU composite/copy is performed only on request. Mapping is asynchronous;
    /// normal frames never transfer pixels to the CPU or wait for the queue.
    /// </summary>
    public Task<byte[]> CaptureRgbaAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Frames == 0 || _compositeGroup == null) throw new InvalidOperationException("No WebGPU frame is available for capture.");
        if (_targetFormat is not (TextureFormat.Rgba8Unorm or TextureFormat.Rgba8UnormSrgb or TextureFormat.Bgra8Unorm or TextureFormat.Bgra8UnormSrgb))
            throw new NotSupportedException($"RGBA8 capture does not support target format {_targetFormat}.");
        if (_captureRequest is { IsCompleted: false }) throw new InvalidOperationException("A WebGPU capture is already in flight.");
        var stride = WebGpuRenderMath.AlignedBytesPerRow(_width);
        GpuTexture? texture = null; GpuBuffer* buffer = null; CommandEncoder* encoder = null;
        try
        {
            texture = new(_api, _device, (uint)_width, (uint)_height, _targetFormat, TextureUsage.RenderAttachment | TextureUsage.CopySrc);
            buffer = CreateBuffer(checked((ulong)stride * (uint)_height), BufferUsage.CopyDst | BufferUsage.MapRead);
            encoder = Require(_api.DeviceCreateCommandEncoder(_device, null), "capture command encoder");
            Composite(encoder, texture.View);
            var source = new ImageCopyTexture { Texture = texture.Texture, Aspect = TextureAspect.All };
            var destination = new ImageCopyBuffer { Buffer = buffer, Layout = new() { BytesPerRow = (uint)stride, RowsPerImage = (uint)_height } };
            var extent = new Extent3D((uint)_width, (uint)_height, 1);
            _api.CommandEncoderCopyTextureToBuffer(encoder, &source, &destination, &extent);
            Submit(encoder);
            var request = new CaptureRequest(_api, buffer, texture, _width, _height, stride,
                _targetFormat is TextureFormat.Bgra8Unorm or TextureFormat.Bgra8UnormSrgb);
            // Transfer ownership before registering a callback: native implementations may complete inline.
            buffer = null; texture = null; _captureRequest = request;
            return request.Start();
        }
        finally
        {
            if (encoder != null) _api.CommandEncoderRelease(encoder);
            if (buffer != null) Release(ref buffer); texture?.Dispose();
        }
    }

    /// <summary>Cancel a timed-out capture while preserving the live render device.</summary>
    public void CancelCapture() => _captureRequest?.Cancel(new OperationCanceledException("WebGPU capture was canceled."));

    private sealed class CaptureRequest(WebGPU api, GpuBuffer* buffer, GpuTexture texture, int width, int height, int stride, bool bgra)
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource<byte[]> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GpuBuffer* _buffer = buffer;
        private GpuTexture? _texture = texture;
        private bool _cancelled, _released;
        public bool IsCompleted => _completion.Task.IsCompleted;
        public Task<byte[]> Start()
        {
            var handle = GCHandle.Alloc(this);
            try
            {
                api.BufferMapAsync(_buffer, MapMode.Read, 0, checked((nuint)stride * (nuint)height),
                    new PfnBufferMapCallback(&Mapped), (void*)GCHandle.ToIntPtr(handle));
            }
            catch (Exception error)
            {
                handle.Free(); Release(); _completion.TrySetException(error);
            }
            return _completion.Task;
        }
        public void Cancel(Exception? reason = null)
        {
            lock (_gate)
            {
                if (_released || _cancelled) return;
                _cancelled = true;
                _completion.TrySetException(reason ?? new ObjectDisposedException(nameof(WebGpuRenderer), "The viewport was unloaded during capture."));
                // Destroy cancels an outstanding map; its completion still owns the GCHandle.
                // Do not free callback userdata before the API delivers that completion.
                api.BufferDestroy(_buffer);
            }
        }
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void Mapped(BufferMapAsyncStatus status, void* userdata)
        {
            var handle = GCHandle.FromIntPtr((nint)userdata);
            try { ((CaptureRequest)handle.Target!).Complete(status); }
            catch { /* Never unwind a managed exception across a native callback boundary. */ }
            finally { handle.Free(); }
        }
        private void Complete(BufferMapAsyncStatus status)
        {
            lock (_gate)
            {
                try
                {
                    if (_cancelled) return;
                    if (status != BufferMapAsyncStatus.Success) throw new InvalidOperationException($"WebGPU capture mapping failed: {status}.");
                    var pointer = api.BufferGetConstMappedRange(_buffer, 0, checked((nuint)stride * (nuint)height));
                    if (pointer == null) throw new InvalidOperationException("WebGPU returned an empty mapped capture.");
                    var output = new byte[checked(width * height * 4)];
                    for (var y = 0; y < height; y++)
                        new ReadOnlySpan<byte>((byte*)pointer + (nint)y * stride, width * 4).CopyTo(output.AsSpan(y * width * 4, width * 4));
                    if (bgra) for (var i = 0; i < output.Length; i += 4) (output[i], output[i + 2]) = (output[i + 2], output[i]);
                    _completion.TrySetResult(output);
                }
                catch (Exception error) { _completion.TrySetException(error); }
                finally { Release(); }
            }
        }
        private void Release()
        {
            if (_released) return; _released = true;
            if (_buffer != null) { api.BufferUnmap(_buffer); api.BufferDestroy(_buffer); api.BufferRelease(_buffer); _buffer = null; }
            _texture?.Dispose(); _texture = null;
        }
    }
}
