using System.Text;
using Silk.NET.WebGPU;
using Uno.UI.Composition.Drawing;
using Uno.UI.Composition.WebGpu;
using Uno.WinUI.Graphics3DWebGpu;
using Windows.Foundation;

namespace NurbsScene.Controls.Drawing;

/// <summary>
/// Vector canvas on Uno's own WebGPU device. Uno records paths/glyphs into a GPU offscreen texture; one
/// texture-load pass resolves that into the element's RGBA target. The explicit resolve is required because
/// native window factories may use BGRA, while WebGpuCanvasElement targets are always RGBA.
/// </summary>
public abstract class WebGpuVectorCanvasElement : WebGpuCanvasElement
{
    private IDrawingFactory? _factory;
    private VectorTargetResolve? _resolve;
    public long DrawingFrames { get; private set; }
    public string? LastDrawingError { get; private set; }
    protected WebGpuVectorCanvasElement() => SizeChanged += (_, _) => Invalidate();
    protected sealed override void Init(WebGpuCanvasContext context)
    {
        try
        {
            var provider = (IGraphicsProvider<IWebGpuDeviceContext>)WebGpuBackend.CreateGraphicsProvider();
            _factory = provider.CreateGraphics(new BorrowedDevice(context));
            _resolve = VectorTargetResolve.Acquire(context);
            LastDrawingError = null;
        }
        catch (Exception error)
        {
            _resolve?.Dispose(); _resolve = null; _factory = null;
            LastDrawingError = error.Message; throw;
        }
    }
    protected sealed override void RenderOverride(WebGpuCanvasContext context)
    {
        if (_factory is null || _resolve is null) return;
        try
        {
            var area = new Size(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));
            // RenderOffscreen is the public nested-render API. BeginPresent would reset shared frame resources
            // and use the native window attachment format against an incompatible RGBA element target.
            using var texture = _factory.RenderOffscreen(context.PixelWidth, context.PixelHeight, session =>
            {
                session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                session.Scale((float)(context.PixelWidth / area.Width), (float)(context.PixelHeight / area.Height));
                Draw(new VectorCanvas(session), area);
            });
            _resolve.Render(context, (WebGpuTexture)texture);
            DrawingFrames++;
        }
        catch (Exception error) { LastDrawingError = error.Message; throw; }
    }
    protected abstract void Draw(VectorCanvas canvas, Size area);
    protected sealed override void OnDestroy(WebGpuCanvasContext context) => ReleaseDrawingResources();
    protected void ReleaseDrawingResources()
    {
        _resolve?.Dispose(); _resolve = null;
        // The factory belongs to Uno and is shared by every canvas/window on this device.
        _factory = null;
    }
    protected override void OnWebGpuUnavailable()
        => LastDrawingError = "This vector canvas requires the WebGPU rendering backend.";

    private sealed unsafe class BorrowedDevice(WebGpuCanvasContext context) : IWebGpuDeviceContext
    {
        public GraphicsContextKind Kind => GraphicsContextKind.WebGpu;
        public WebGPU Api => context.Api;
        public Instance* Instance => context.Instance;
        public Adapter* Adapter => context.Adapter;
        public Device* Device => context.Device;
        public Queue* Queue => context.Queue;
        public TextureFormat ColorFormat => context.TargetFormat;
        public void Dispose() { }
    }
}

/// <summary>One immutable conversion pipeline per device/target format, shared by graphs, overlays and icons.</summary>
internal sealed unsafe class VectorTargetResolve : IDisposable
{
    private static readonly Dictionary<(nint Device, TextureFormat Format), VectorTargetResolve> Shared = [];
    private readonly (nint Device, TextureFormat Format) _key;
    private readonly WebGPU _api;
    private readonly Device* _device;
    private RenderPipeline* _pipeline;
    private BindGroupLayout* _layout;
    private int _references = 1;
    private const string Shader = """
        @group(0) @binding(0) var source: texture_2d<f32>;
        @vertex fn vertex_main(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
            let p = vec2f(f32((i << 1u) & 2u), f32(i & 2u));
            return vec4f(p * 2.0 - 1.0, 0.0, 1.0);
        }
        @fragment fn fragment_main(@builtin(position) p: vec4f) -> @location(0) vec4f {
            return textureLoad(source, vec2i(p.xy), 0);
        }
        """;
    public static VectorTargetResolve Acquire(WebGpuCanvasContext context)
    {
        lock (Shared)
        {
            var key = ((nint)context.Device, context.TargetFormat);
            if (Shared.TryGetValue(key, out var cached)) { cached._references++; return cached; }
            var result = new VectorTargetResolve(context); Shared.Add(key, result); return result;
        }
    }
    private VectorTargetResolve(WebGpuCanvasContext context)
    {
        _api = context.Api; _device = context.Device; _key = ((nint)_device, context.TargetFormat);
        ShaderModule* module = null;
        try
        {
            var code = Encoding.UTF8.GetBytes(Shader + "\0");
            var vertex = Encoding.UTF8.GetBytes("vertex_main\0");
            var fragment = Encoding.UTF8.GetBytes("fragment_main\0");
            fixed (byte* source = code, vertexEntry = vertex, fragmentEntry = fragment)
            {
                var wgsl = new ShaderModuleWGSLDescriptor { Chain = new() { SType = SType.ShaderModuleWgslDescriptor }, Code = source };
                var descriptor = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&wgsl };
                module = _api.DeviceCreateShaderModule(_device, &descriptor);
                var target = new ColorTargetState { Format = context.TargetFormat, WriteMask = ColorWriteMask.All };
                var fs = new FragmentState { Module = module, EntryPoint = fragmentEntry, TargetCount = 1, Targets = &target };
                var pipeline = new RenderPipelineDescriptor
                {
                    Vertex = new() { Module = module, EntryPoint = vertexEntry }, Fragment = &fs,
                    Primitive = new() { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                    Multisample = new() { Count = 1, Mask = uint.MaxValue }
                };
                _pipeline = _api.DeviceCreateRenderPipeline(_device, &pipeline);
                if (_pipeline == null) throw new InvalidOperationException("The vector target resolve pipeline could not be created.");
                _layout = _api.RenderPipelineGetBindGroupLayout(_pipeline, 0);
            }
        }
        catch
        {
            if (_layout != null) _api.BindGroupLayoutRelease(_layout);
            if (_pipeline != null) _api.RenderPipelineRelease(_pipeline);
            throw;
        }
        finally { if (module != null) _api.ShaderModuleRelease(module); }
    }
    public void Render(WebGpuCanvasContext context, WebGpuTexture texture)
    {
        BindGroup* group = null; CommandEncoder* encoder = null;
        RenderPassEncoder* pass = null; CommandBuffer* commands = null;
        try
        {
            var entry = new BindGroupEntry { Binding = 0, TextureView = texture.View };
            var groupDescriptor = new BindGroupDescriptor { Layout = _layout, EntryCount = 1, Entries = &entry };
            group = _api.DeviceCreateBindGroup(_device, &groupDescriptor);
            encoder = _api.DeviceCreateCommandEncoder(_device, null);
            var color = new RenderPassColorAttachment
            {
                View = context.TargetView, DepthSlice = uint.MaxValue, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store,
                ClearValue = new() { R = 0, G = 0, B = 0, A = 0 }
            };
            var descriptor = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &color };
            pass = _api.CommandEncoderBeginRenderPass(encoder, &descriptor);
            _api.RenderPassEncoderSetPipeline(pass, _pipeline);
            _api.RenderPassEncoderSetBindGroup(pass, 0, group, 0, null);
            _api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
            _api.RenderPassEncoderEnd(pass);
            commands = _api.CommandEncoderFinish(encoder, null);
            _api.QueueSubmit(context.Queue, 1, &commands);
        }
        finally
        {
            if (commands != null) _api.CommandBufferRelease(commands);
            if (pass != null) _api.RenderPassEncoderRelease(pass);
            if (encoder != null) _api.CommandEncoderRelease(encoder);
            if (group != null) _api.BindGroupRelease(group);
        }
    }
    public void Dispose()
    {
        lock (Shared)
        {
            if (--_references != 0) return;
            Shared.Remove(_key);
            if (_layout != null) { _api.BindGroupLayoutRelease(_layout); _layout = null; }
            if (_pipeline != null) { _api.RenderPipelineRelease(_pipeline); _pipeline = null; }
        }
    }
}
