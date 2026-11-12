using System.Text;
using Silk.NET.WebGPU;
using GpuBuffer = Silk.NET.WebGPU.Buffer;

namespace NurbsScene.Rendering;

public sealed unsafe partial class WebGpuRenderer
{
    private const uint Samples = 4;
    private const int ShadowSize = 2048;
    private readonly WebGPU _api;
    private readonly Device* _device;
    private readonly Queue* _queue;
    private readonly TextureFormat _targetFormat;
    private readonly Func<nint, int, Task<byte[]>>? _browserReadback;
    private readonly uint _uniformAlignment;
    private readonly uint _maximumTextureSize;
    private readonly ulong _maximumBufferSize;
    private readonly List<nint> _layouts = [];
    private readonly List<nint> _pipelineLayouts = [];
    private readonly List<nint> _renderPipelines = [];
    private readonly List<nint> _computePipelines = [];
    private BindGroupLayout* _frameLayout, _objectLayout, _shadowLayout, _bloomLayout, _compositeLayout;
    private RenderPipeline* _surfacePipeline, _linePipeline, _ghostPipeline, _shadowPipeline, _compositePipeline;
    private ComputePipeline* _downsamplePipeline, _horizontalPipeline, _verticalPipeline;
    private Sampler* _shadowSampler, _linearSampler;
    private GpuBuffer* _framesBuffer, _objectsBuffer, _bloomParameters, _compositeParameters;
    private BindGroup* _frameGroup, _shadowGroup, _objectGroup, _downsampleGroup, _horizontalGroup, _verticalGroup, _compositeGroup;
    private uint _frameStride, _objectStride;
    private int _objectCapacity;
    private GpuTexture? _shadow, _hdr, _hdrMultisample, _depth, _accum, _accumMultisample, _reveal, _revealMultisample, _bloomA, _bloomB;
    private int _width, _height;
    private bool _disposed, _targetsHaveTransparency;

    /// <summary>All context objects are borrowed from Uno. Only objects created by this renderer are released.</summary>
    public WebGpuRenderer(WebGPU api, Device* device, Queue* queue, TextureFormat targetFormat,
        Func<nint, int, Task<byte[]>>? browserReadback = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (device == null || queue == null) throw new ArgumentException("A live WebGPU device and queue are required.");
        _api = api; _device = device; _queue = queue; _targetFormat = targetFormat; _browserReadback = browserReadback;
        SupportedLimits supported = default;
        if (!api.DeviceGetLimits(device, &supported)) throw new InvalidOperationException("Cannot query WebGPU device limits.");
        _uniformAlignment = Math.Max(256, supported.Limits.MinUniformBufferOffsetAlignment);
        _maximumTextureSize = supported.Limits.MaxTextureDimension2D;
        _maximumBufferSize = supported.Limits.MaxBufferSize;
        _frameStride = Align(256, _uniformAlignment); _objectStride = Align(32, _uniformAlignment);
        try { InitializeResources(); }
        catch { Dispose(); throw; }
    }

    private static uint Align(uint size, uint alignment) => checked((size + alignment - 1) / alignment * alignment);
    private static T* Require<T>(T* value, string name) where T : unmanaged => value != null ? value : throw new InvalidOperationException($"WebGPU could not create {name}.");

    private sealed class GpuTexture : IDisposable
    {
        private readonly WebGPU _api;
        public Texture* Texture { get; private set; }
        public TextureView* View { get; private set; }
        public GpuTexture(WebGPU api, Device* device, uint width, uint height, TextureFormat format, TextureUsage usage,
            uint samples = 1, uint mipLevels = 1, uint layers = 1, TextureViewDimension? viewDimension = null)
        {
            _api = api;
            var descriptor = new TextureDescriptor
            {
                Dimension = TextureDimension.Dimension2D, Size = new(width, height, layers), Format = format,
                MipLevelCount = mipLevels, SampleCount = samples, Usage = usage
            };
            Texture = Require(api.DeviceCreateTexture(device, &descriptor), "texture");
            try
            {
                if (viewDimension is null) View = Require(api.TextureCreateView(Texture, null), "texture view");
                else
                {
                    var view = new TextureViewDescriptor { Format = format, Dimension = viewDimension.Value,
                        MipLevelCount = mipLevels, ArrayLayerCount = layers, Aspect = TextureAspect.All };
                    View = Require(api.TextureCreateView(Texture, &view), "texture view");
                }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (View != null) { _api.TextureViewRelease(View); View = null; }
            if (Texture != null) { _api.TextureDestroy(Texture); _api.TextureRelease(Texture); Texture = null; }
        }
    }

    private GpuBuffer* CreateBuffer(ulong bytes, BufferUsage usage)
    {
        if (bytes > _maximumBufferSize) throw new ArgumentOutOfRangeException(nameof(bytes), "Buffer exceeds the adapter limit.");
        var descriptor = new BufferDescriptor { Size = Math.Max(4, bytes), Usage = usage };
        return Require(_api.DeviceCreateBuffer(_device, &descriptor), "buffer");
    }
    private void Release(ref GpuBuffer* buffer)
    {
        if (buffer == null) return;
        _api.BufferDestroy(buffer); _api.BufferRelease(buffer); buffer = null;
    }
    private void Release(ref BindGroup* group) { if (group != null) _api.BindGroupRelease(group); group = null; }
    private void Write(GpuBuffer* buffer, ReadOnlySpan<float> values)
    {
        if (values.IsEmpty) return;
        fixed (float* pointer = values) _api.QueueWriteBuffer(_queue, buffer, 0, pointer, checked((nuint)values.Length * 4));
    }
    private BindGroupLayout* Layout(ReadOnlySpan<BindGroupLayoutEntry> entries)
    {
        fixed (BindGroupLayoutEntry* pointer = entries)
        {
            var descriptor = new BindGroupLayoutDescriptor { EntryCount = (nuint)entries.Length, Entries = pointer };
            var result = Require(_api.DeviceCreateBindGroupLayout(_device, &descriptor), "bind-group layout");
            _layouts.Add((nint)result); return result;
        }
    }
    private PipelineLayout* PipelineLayout(ReadOnlySpan<nint> layouts)
    {
        fixed (nint* pointer = layouts)
        {
            var descriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = (nuint)layouts.Length, BindGroupLayouts = (BindGroupLayout**)pointer };
            var result = Require(_api.DeviceCreatePipelineLayout(_device, &descriptor), "pipeline layout");
            _pipelineLayouts.Add((nint)result); return result;
        }
    }
    private BindGroup* Group(BindGroupLayout* layout, ReadOnlySpan<BindGroupEntry> entries)
    {
        fixed (BindGroupEntry* pointer = entries)
        {
            var descriptor = new BindGroupDescriptor { Layout = layout, EntryCount = (nuint)entries.Length, Entries = pointer };
            return Require(_api.DeviceCreateBindGroup(_device, &descriptor), "bind group");
        }
    }
    private static BindGroupLayoutEntry Uniform(uint binding, ulong size, ShaderStage stages, bool dynamic = false) => new()
    {
        Binding = binding, Visibility = stages,
        Buffer = new() { Type = BufferBindingType.Uniform, MinBindingSize = size, HasDynamicOffset = dynamic }
    };
    private static BindGroupLayoutEntry Sampled(uint binding, ShaderStage stages, TextureSampleType type = TextureSampleType.Float) => new()
    {
        Binding = binding, Visibility = stages, Texture = new() { SampleType = type, ViewDimension = TextureViewDimension.Dimension2D }
    };
    private static BindGroupLayoutEntry SamplerEntry(uint binding, SamplerBindingType type) => new()
    {
        Binding = binding, Visibility = ShaderStage.Fragment, Sampler = new() { Type = type }
    };
    private ShaderModule* Shader(string name)
    {
        using var stream = typeof(WebGpuRenderer).Assembly.GetManifestResourceStream($"NurbsScene.Rendering.Shaders.{name}")
            ?? throw new InvalidOperationException($"Missing WGSL shader {name}.");
        using var reader = new StreamReader(stream);
        fixed (byte* code = Encoding.UTF8.GetBytes(reader.ReadToEnd() + '\0'))
        {
            var wgsl = new ShaderModuleWGSLDescriptor { Chain = new() { SType = SType.ShaderModuleWgslDescriptor }, Code = code };
            var descriptor = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&wgsl };
            return Require(_api.DeviceCreateShaderModule(_device, &descriptor), name);
        }
    }

    private void InitializeResources()
    {
        _frameLayout = Layout([Uniform(0, 256, ShaderStage.Vertex | ShaderStage.Fragment, true), Sampled(1, ShaderStage.Fragment, TextureSampleType.Depth), SamplerEntry(2, SamplerBindingType.Comparison),
            new() { Binding = 3, Visibility = ShaderStage.Fragment, Texture = new() { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.DimensionCube } },
            SamplerEntry(4, SamplerBindingType.Filtering), Sampled(5, ShaderStage.Fragment), Uniform(6, 144, ShaderStage.Fragment)]);
        _objectLayout = Layout([Uniform(0, 32, ShaderStage.Vertex | ShaderStage.Fragment, true)]);
        _shadowLayout = Layout([Uniform(0, 256, ShaderStage.Vertex | ShaderStage.Fragment, true)]);
        _bloomLayout = Layout([Sampled(0, ShaderStage.Compute), new() { Binding = 1, Visibility = ShaderStage.Compute, StorageTexture = new() { Access = StorageTextureAccess.WriteOnly, Format = TextureFormat.Rgba16float, ViewDimension = TextureViewDimension.Dimension2D } }, Uniform(2, 32, ShaderStage.Compute), Sampled(3, ShaderStage.Compute), Sampled(4, ShaderStage.Compute)]);
        _compositeLayout = Layout([Sampled(0, ShaderStage.Fragment), Sampled(1, ShaderStage.Fragment), Sampled(2, ShaderStage.Fragment), Sampled(3, ShaderStage.Fragment), SamplerEntry(4, SamplerBindingType.Filtering), Uniform(5, 32, ShaderStage.Fragment)]);
        var sceneLayout = PipelineLayout([(nint)_frameLayout, (nint)_objectLayout]);
        var shadowLayout = PipelineLayout([(nint)_shadowLayout]);
        var bloomLayout = PipelineLayout([(nint)_bloomLayout]);
        var compositeLayout = PipelineLayout([(nint)_compositeLayout]);
        var scene = Shader("scene.wgsl");
        try
        {
            _surfacePipeline = RenderPipeline(scene, sceneLayout, "surfaceVertex", "surfaceFragment", false, false, false);
            _linePipeline = RenderPipeline(scene, sceneLayout, "lineVertex", "lineFragment", true, false, false);
            _ghostPipeline = RenderPipeline(scene, sceneLayout, "surfaceVertex", "ghostFragment", false, true, false);
            _shadowPipeline = RenderPipeline(scene, shadowLayout, "shadowVertex", "shadowFragment", false, false, true);
        }
        finally { _api.ShaderModuleRelease(scene); }
        var bloom = Shader("bloom.wgsl");
        try
        {
            _downsamplePipeline = ComputePipeline(bloom, bloomLayout, "downsample");
            _horizontalPipeline = ComputePipeline(bloom, bloomLayout, "blurHorizontal");
            _verticalPipeline = ComputePipeline(bloom, bloomLayout, "blurVertical");
        }
        finally { _api.ShaderModuleRelease(bloom); }
        var composite = Shader("composite.wgsl");
        try { _compositePipeline = CompositePipeline(composite, compositeLayout); }
        finally { _api.ShaderModuleRelease(composite); }
        var sampler = new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge, AddressModeV = AddressMode.ClampToEdge, AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear, MinFilter = FilterMode.Linear, MipmapFilter = MipmapFilterMode.Nearest,
            LodMaxClamp = 32, MaxAnisotropy = 1, Compare = CompareFunction.LessEqual
        };
        _shadowSampler = Require(_api.DeviceCreateSampler(_device, &sampler), "shadow sampler");
        sampler.Compare = CompareFunction.Undefined;
        _linearSampler = Require(_api.DeviceCreateSampler(_device, &sampler), "linear sampler");
        _shadow = new(_api, _device, ShadowSize, ShadowSize, TextureFormat.Depth32float, TextureUsage.RenderAttachment | TextureUsage.TextureBinding);
        _framesBuffer = CreateBuffer(_frameStride * 5ul, BufferUsage.Uniform | BufferUsage.CopyDst);
        _bloomParameters = CreateBuffer(32, BufferUsage.Uniform | BufferUsage.CopyDst);
        _compositeParameters = CreateBuffer(32, BufferUsage.Uniform | BufferUsage.CopyDst);
        InitializeEnvironment();
        _frameGroup = Group(_frameLayout, [new() { Binding = 0, Buffer = _framesBuffer, Size = 256 }, new() { Binding = 1, TextureView = _shadow.View }, new() { Binding = 2, Sampler = _shadowSampler },
            new() { Binding = 3, TextureView = _environment!.View }, new() { Binding = 4, Sampler = _environmentSampler },
            new() { Binding = 5, TextureView = _environmentBrdf!.View }, new() { Binding = 6, Buffer = _environmentIrradiance, Size = 144 }]);
        _shadowGroup = Group(_shadowLayout, [new() { Binding = 0, Buffer = _framesBuffer, Size = 256 }]);
        EnsureObjectCapacity(16);
    }

    private static BlendState AlphaBlend => new()
    {
        Color = new() { Operation = BlendOperation.Add, SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha },
        Alpha = new() { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha }
    };

    private RenderPipeline* RenderPipeline(ShaderModule* module, PipelineLayout* layout, string vertexName, string fragmentName, bool line, bool ghost, bool shadow)
    {
        var attributes = stackalloc VertexAttribute[2];
        attributes[0] = new() { ShaderLocation = 0, Format = VertexFormat.Float32x3, Offset = 0 };
        attributes[1] = new() { ShaderLocation = 1, Format = VertexFormat.Float32x3, Offset = 12 };
        var buffer = new VertexBufferLayout { ArrayStride = 24, StepMode = line ? VertexStepMode.Instance : VertexStepMode.Vertex, AttributeCount = 2, Attributes = attributes };
        var alpha = AlphaBlend;
        var add = new BlendState { Color = new() { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.One }, Alpha = new() { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.One } };
        var reveal = new BlendState { Color = new() { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.OneMinusSrcAlpha }, Alpha = new() { Operation = BlendOperation.Add, SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.OneMinusSrcAlpha } };
        var targets = stackalloc ColorTargetState[2];
        targets[0] = new() { Format = TextureFormat.Rgba16float, WriteMask = ColorWriteMask.All, Blend = ghost ? &add : line ? &alpha : null };
        targets[1] = new() { Format = TextureFormat.R8Unorm, WriteMask = ColorWriteMask.All, Blend = &reveal };
        var depth = new DepthStencilState
        {
            Format = TextureFormat.Depth32float, DepthWriteEnabled = !ghost && !line,
            DepthCompare = shadow ? CompareFunction.LessEqual : CompareFunction.GreaterEqual,
            StencilFront = new() { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
            StencilBack = new() { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
            DepthBias = shadow ? 2 : 0, DepthBiasSlopeScale = shadow ? 2 : 0
        };
        fixed (byte* vertex = Encoding.UTF8.GetBytes(vertexName + '\0'))
        fixed (byte* fragment = Encoding.UTF8.GetBytes(fragmentName + '\0'))
        {
            var fragmentState = new FragmentState { Module = module, EntryPoint = fragment, TargetCount = shadow ? 0u : ghost ? 2u : 1u, Targets = shadow ? null : targets };
            var descriptor = new RenderPipelineDescriptor
            {
                Label = fragment, Layout = layout,
                Vertex = new() { Module = module, EntryPoint = vertex, BufferCount = 1, Buffers = &buffer },
                Fragment = &fragmentState, DepthStencil = &depth,
                Primitive = new() { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                Multisample = new() { Count = shadow ? 1 : Samples, Mask = uint.MaxValue }
            };
            var result = Require(_api.DeviceCreateRenderPipeline(_device, &descriptor), fragmentName);
            _renderPipelines.Add((nint)result); return result;
        }
    }
    private ComputePipeline* ComputePipeline(ShaderModule* module, PipelineLayout* layout, string entry)
    {
        fixed (byte* name = Encoding.UTF8.GetBytes(entry + '\0'))
        {
            var descriptor = new ComputePipelineDescriptor { Label = name, Layout = layout, Compute = new() { Module = module, EntryPoint = name } };
            var result = Require(_api.DeviceCreateComputePipeline(_device, &descriptor), entry);
            _computePipelines.Add((nint)result); return result;
        }
    }
    private RenderPipeline* CompositePipeline(ShaderModule* module, PipelineLayout* layout)
    {
        fixed (byte* vertex = "fullscreenVertex\0"u8)
        fixed (byte* fragment = "compositeFragment\0"u8)
        {
            var target = new ColorTargetState { Format = _targetFormat, WriteMask = ColorWriteMask.All };
            var stage = new FragmentState { Module = module, EntryPoint = fragment, TargetCount = 1, Targets = &target };
            var descriptor = new RenderPipelineDescriptor
            {
                Layout = layout, Vertex = new() { Module = module, EntryPoint = vertex }, Fragment = &stage,
                Primitive = new() { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                Multisample = new() { Count = 1, Mask = uint.MaxValue }
            };
            var result = Require(_api.DeviceCreateRenderPipeline(_device, &descriptor), "HDR composition pipeline");
            _renderPipelines.Add((nint)result); return result;
        }
    }

    private void EnsureObjectCapacity(int count)
    {
        if (count <= _objectCapacity) return;
        var capacity = Math.Max(16, checked((int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)count)));
        var next = CreateBuffer(checked((ulong)capacity * _objectStride), BufferUsage.Uniform | BufferUsage.CopyDst);
        BindGroup* group = null;
        try { group = Group(_objectLayout, [new() { Binding = 0, Buffer = next, Size = 32 }]); }
        catch { Release(ref next); throw; }
        Release(ref _objectGroup); Release(ref _objectsBuffer);
        _objectsBuffer = next; _objectGroup = group; _objectCapacity = capacity;
        _objectData = new float[checked(capacity * (int)_objectStride / 4)];
    }
    private void Resize(int width, int height)
    {
        if (_width == width && _height == height && _targetsHaveTransparency == _hasTransparency) return;
        if (width < 1 || height < 1 || width > _maximumTextureSize || height > _maximumTextureSize)
            throw new ArgumentOutOfRangeException(nameof(width), "Viewport exceeds WebGPU texture limits.");
        ReleaseTargets();
        try
        {
            var render = TextureUsage.RenderAttachment;
            var sampled = render | TextureUsage.TextureBinding;
            _hdr = new(_api, _device, (uint)width, (uint)height, TextureFormat.Rgba16float, sampled);
            _hdrMultisample = new(_api, _device, (uint)width, (uint)height, TextureFormat.Rgba16float, render, Samples);
            _depth = new(_api, _device, (uint)width, (uint)height, TextureFormat.Depth32float, render, Samples);
            // Weighted OIT costs 45 attachment bytes per pixel at 4x MSAA.
            // Keep only neutral 1x1 targets when no transparent surfaces exist.
            var oitWidth = _hasTransparency ? (uint)width : 1u;
            var oitHeight = _hasTransparency ? (uint)height : 1u;
            _accum = new(_api, _device, oitWidth, oitHeight, TextureFormat.Rgba16float, sampled);
            _accumMultisample = new(_api, _device, oitWidth, oitHeight, TextureFormat.Rgba16float, render, Samples);
            _reveal = new(_api, _device, oitWidth, oitHeight, TextureFormat.R8Unorm, sampled);
            _revealMultisample = new(_api, _device, oitWidth, oitHeight, TextureFormat.R8Unorm, render, Samples);
            var bloomUsage = TextureUsage.TextureBinding | TextureUsage.StorageBinding;
            _bloomA = new(_api, _device, (uint)(width + 1) / 2, (uint)(height + 1) / 2, TextureFormat.Rgba16float, bloomUsage);
            _bloomB = new(_api, _device, (uint)(width + 1) / 2, (uint)(height + 1) / 2, TextureFormat.Rgba16float, bloomUsage);
            _downsampleGroup = BloomGroup(_hdr.View, _bloomA.View);
            _horizontalGroup = BloomGroup(_bloomA.View, _bloomB.View);
            _verticalGroup = BloomGroup(_bloomB.View, _bloomA.View);
            _compositeGroup = Group(_compositeLayout, [new() { Binding = 0, TextureView = _hdr.View }, new() { Binding = 1, TextureView = _accum.View }, new() { Binding = 2, TextureView = _reveal.View }, new() { Binding = 3, TextureView = _bloomA.View }, new() { Binding = 4, Sampler = _linearSampler }, new() { Binding = 5, Buffer = _compositeParameters, Size = 32 }]);
            _width = width; _height = height; _targetsHaveTransparency = _hasTransparency;
        }
        catch { ReleaseTargets(); throw; }
    }
    private BindGroup* BloomGroup(TextureView* source, TextureView* destination) => Group(_bloomLayout,
        [new() { Binding = 0, TextureView = source }, new() { Binding = 1, TextureView = destination }, new() { Binding = 2, Buffer = _bloomParameters, Size = 32 }, new() { Binding = 3, TextureView = _accum!.View }, new() { Binding = 4, TextureView = _reveal!.View }]);
    private void ReleaseTargets()
    {
        Release(ref _downsampleGroup); Release(ref _horizontalGroup); Release(ref _verticalGroup); Release(ref _compositeGroup);
        _hdr?.Dispose(); _hdr = null; _hdrMultisample?.Dispose(); _hdrMultisample = null; _depth?.Dispose(); _depth = null;
        _accum?.Dispose(); _accum = null; _accumMultisample?.Dispose(); _accumMultisample = null;
        _reveal?.Dispose(); _reveal = null; _revealMultisample?.Dispose(); _revealMultisample = null;
        _bloomA?.Dispose(); _bloomA = null; _bloomB?.Dispose(); _bloomB = null;
        _width = _height = 0; _targetsHaveTransparency = false;
    }
}
