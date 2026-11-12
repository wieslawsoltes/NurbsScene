using Silk.NET.WebGPU;
using GpuBuffer = Silk.NET.WebGPU.Buffer;

namespace NurbsScene.Rendering;

public sealed unsafe partial class WebGpuRenderer
{
    private GpuTexture? _environment, _environmentBrdf;
    private Sampler* _environmentSampler;
    private GpuBuffer* _environmentIrradiance;

    /// <summary>SHA-256 of the canonical decoded HDR studio integration payload.</summary>
    public string EnvironmentSourceSha256 => StudioEnvironmentData.PayloadSha256;

    private void InitializeEnvironment()
    {
        var data = StudioEnvironmentData.Shared;
        var usage = TextureUsage.TextureBinding | TextureUsage.CopyDst;
        _environment = new(_api, _device, (uint)data.CubeSize, (uint)data.CubeSize,
            TextureFormat.Rgba16float, usage, mipLevels: (uint)data.MipCount, layers: 6, viewDimension: TextureViewDimension.DimensionCube);
        for (var level = 0; level < data.Mips.Count; level++)
        {
            var mip = data.Mips[level];
            UploadEnvironmentTexture(_environment.Texture, mip.Pixels.Span, (uint)mip.Size, (uint)level, 6);
        }
        _environmentBrdf = new(_api, _device, (uint)data.LutSize, (uint)data.LutSize, TextureFormat.Rgba16float, usage);
        UploadEnvironmentTexture(_environmentBrdf.Texture, data.BrdfLut.Span, (uint)data.LutSize, 0, 1);
        _environmentIrradiance = CreateBuffer(144, BufferUsage.Uniform | BufferUsage.CopyDst);
        fixed (byte* bytes = data.SphericalHarmonics.Span)
            _api.QueueWriteBuffer(_queue, _environmentIrradiance, 0, bytes, 144);
        var sampler = new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge, AddressModeV = AddressMode.ClampToEdge, AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear, MinFilter = FilterMode.Linear, MipmapFilter = MipmapFilterMode.Linear,
            LodMaxClamp = data.MipCount - 1, MaxAnisotropy = 1, Compare = CompareFunction.Undefined
        };
        _environmentSampler = Require(_api.DeviceCreateSampler(_device, &sampler), "prefiltered studio environment sampler");
    }

    private void UploadEnvironmentTexture(Texture* texture, ReadOnlySpan<byte> pixels, uint size, uint mip, uint layers)
    {
        var destination = new ImageCopyTexture { Texture = texture, MipLevel = mip, Aspect = TextureAspect.All };
        // QueueWriteTexture accepts packed rows; unlike buffer-to-texture copies,
        // it has no 256-byte row-alignment requirement for the smaller mips.
        var layout = new TextureDataLayout { BytesPerRow = checked(size * 8), RowsPerImage = size };
        var extent = new Extent3D(size, size, layers);
        fixed (byte* bytes = pixels)
            _api.QueueWriteTexture(_queue, &destination, bytes, (nuint)pixels.Length, &layout, &extent);
    }

    private void DisposeEnvironment()
    {
        _environment?.Dispose(); _environment = null;
        _environmentBrdf?.Dispose(); _environmentBrdf = null;
        Release(ref _environmentIrradiance);
        if (_environmentSampler != null) _api.SamplerRelease(_environmentSampler);
        _environmentSampler = null;
    }
}
