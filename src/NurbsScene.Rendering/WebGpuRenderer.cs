using System.Diagnostics;
using NurbsScene.Geometry;
using Silk.NET.WebGPU;
using GpuBuffer = Silk.NET.WebGPU.Buffer;

namespace NurbsScene.Rendering;

/// <summary>Physically based studio presentation. Values describe display only, never exact geometry.</summary>
public sealed record WebGpuRenderSettings
{
    public float Exposure { get; init; } = 1;
    public float Metallic { get; init; } = .12f;
    public float Roughness { get; init; } = .36f;
    public float AmbientIntensity { get; init; } = .55f;
    public float LightIntensity { get; init; } = 3.2f;
    public float BloomStrength { get; init; } = .055f;
    public bool Shadows { get; init; } = true;
    public Vec3 LightDirection { get; init; } = new(-.45, -.6, 1);
    /// <summary>Reject invalid settings before replacing the renderer's retained configuration.</summary>
    public void Validate()
    {
        if (!float.IsFinite(Exposure) || Exposure <= 0 || Exposure > 32
            || !float.IsFinite(Metallic) || Metallic < 0 || Metallic > 1
            || !float.IsFinite(Roughness) || Roughness < .04f || Roughness > 1
            || !float.IsFinite(AmbientIntensity) || AmbientIntensity < 0 || AmbientIntensity > 32
            || !float.IsFinite(LightIntensity) || LightIntensity < 0 || LightIntensity > 128
            || !float.IsFinite(BloomStrength) || BloomStrength < 0 || BloomStrength > 1
            || !LightDirection.IsFinite || LightDirection.Length < 1e-12)
            throw new ArgumentException("Invalid WebGPU studio lighting settings.");
    }
}

/// <summary>
/// Shared-device WebGPU CAD renderer: cached immutable geometry, reverse-Z depth,
/// HDR PBR, shadow mapping, multisampling, weighted blended transparency and compute bloom.
/// All methods except asynchronous capture completion run on the owning render thread.
/// </summary>
public sealed unsafe partial class WebGpuRenderer : IDisposable
{
    private readonly Dictionary<Guid, GpuMesh> _meshes = [];
    private readonly Dictionary<string, GpuLines> _grids = [];
    private readonly List<Guid> _removed = [];
    private readonly List<DrawItem> _draws = [];
    private readonly List<PreparedView> _views = [];
    private readonly List<DrawItem>[] _visible = [[], [], [], []];
    private HashSet<Guid> _casters = [], _nextCasters = [];
    private DisplayScene? _lastScene;
    private long _meshRevision = -1, _itemRevision = -1, _shadowRevision = -1;
    private Vec3 _origin = new(double.NaN, 0, 0);
    private (Vec3 Normal, double Offset)? _shadowClip;
    private DisplayMode _itemMode;
    private Vec3 _shadowLight;
    private float _itemMetallic = float.NaN, _itemRoughness = float.NaN;
    private float[] _objectData = [], _frameData = [];
    private bool _shadowValid, _shadowEnabled, _hasTransparency;
    private CaptureRequest? _captureRequest;

    public WebGpuRenderSettings Settings { get; set; } = new();
    public string Backend => "WebGPU / Uno shared device / WGSL / HDR 4x MSAA";
    public long Frames { get; private set; }
    public int TriangleCount { get; private set; }
    public int LineCount { get; private set; }
    public int DrawCalls { get; private set; }
    public int CulledItems { get; private set; }
    public long GeometryUploadBytes { get; private set; }
    public long GeometryUploadCount { get; private set; }
    public long ShadowPassCount { get; private set; }
    public long ShadowCasterDrawCount { get; private set; }
    public long MaterialUploadCount { get; private set; }
    public double LastSubmissionMilliseconds { get; private set; }

    private sealed class GpuLines(GpuBuffer* buffer, uint count)
    {
        public GpuBuffer* Buffer = buffer;
        public uint Count = count;
    }
    private sealed class GpuMesh(DisplayMesh source, GpuBuffer* vertices, GpuBuffer* indices, GpuLines lines)
    {
        public DisplayMesh Source = source;
        public GpuBuffer* Vertices = vertices;
        public GpuBuffer* Indices = indices;
        public GpuLines Lines = lines;
    }
    private sealed record DrawItem(DisplayItem Item, GpuMesh Mesh, uint SurfaceOffset, uint LineOffset);
    private sealed record PreparedView(ViewportRectangle View, uint FrameOffset, List<DrawItem> Draws);

    private GpuLines UploadLines(float[] values)
    {
        if (values.Length % 6 != 0) throw new ArgumentException("Line vertices must contain endpoint pairs.");
        var buffer = CreateBuffer((ulong)values.Length * 4, BufferUsage.Vertex | BufferUsage.CopyDst);
        try { Write(buffer, values); GeometryUploadBytes += values.Length * 4L; return new(buffer, (uint)values.Length / 6); }
        catch { Release(ref buffer); throw; }
    }
    private GpuMesh Upload(DisplayMesh source)
    {
        GpuBuffer* vertices = null; GpuBuffer* indices = null; GpuLines? lines = null;
        try
        {
            vertices = CreateBuffer((ulong)source.Vertices.Length * 4, BufferUsage.Vertex | BufferUsage.CopyDst);
            indices = CreateBuffer((ulong)source.Indices.Length * 4, BufferUsage.Index | BufferUsage.CopyDst);
            Write(vertices, source.Vertices);
            if (source.Indices.Length > 0)
                fixed (uint* pointer = source.Indices) _api.QueueWriteBuffer(_queue, indices, 0, pointer, checked((nuint)source.Indices.Length * 4));
            lines = UploadLines(source.Lines);
            GeometryUploadBytes += (source.Vertices.Length + source.Indices.Length) * 4L; GeometryUploadCount++;
            return new(source, vertices, indices, lines);
        }
        catch { Release(ref vertices); Release(ref indices); if (lines is not null) Release(ref lines.Buffer); throw; }
    }
    private void Delete(GpuMesh mesh) { Release(ref mesh.Vertices); Release(ref mesh.Indices); Release(ref mesh.Lines.Buffer); }
    private void Synchronize(DisplayScene scene, WebGpuRenderSettings settings)
    {
        var sceneChanged = !ReferenceEquals(scene, _lastScene);
        if (sceneChanged || _meshRevision != scene.MeshRevision)
        {
            _removed.Clear();
            foreach (var (id, mesh) in _meshes)
                if (!scene.Meshes.TryGetValue(id, out var current) || !ReferenceEquals(current, mesh.Source)) _removed.Add(id);
            foreach (var id in _removed) { Delete(_meshes[id]); _meshes.Remove(id); }
            foreach (var (id, mesh) in scene.Meshes) if (!_meshes.ContainsKey(id)) _meshes.Add(id, Upload(mesh));
            _meshRevision = scene.MeshRevision;
        }
        if (_origin != scene.Origin)
        {
            foreach (var grid in _grids.Values) Release(ref grid.Buffer);
            _grids.Clear(); _origin = scene.Origin;
        }
        if (sceneChanged || _itemRevision != scene.Revision || _itemMode != scene.Mode
            || _itemMetallic != settings.Metallic || _itemRoughness != settings.Roughness)
        {
            EnsureObjectCapacity(checked(scene.Items.Count * 2 + 1));
            SetObject(0, new(.46f, .50f, .54f, .48f), .8f, settings);
            _draws.Clear();
            _nextCasters.Clear();
            _hasTransparency = false;
            for (var i = 0; i < scene.Items.Count; i++)
            {
                var item = scene.Items[i]; var surfaceSlot = 1 + i * 2; var lineSlot = surfaceSlot + 1;
                var color = item.Selected ? new RenderColor(.96f, .68f, .12f, item.Color.A) : item.Color;
                if (scene.Mode == DisplayMode.Ghosted) color = color with { A = color.A * .35f };
                SetObject(surfaceSlot, color, 1, settings);
                SetObject(lineSlot, item.Selected ? new(1, .62f, .03f) : new(.13f, .27f, .27f, .78f), item.Selected ? 2 : 1.15f, settings);
                var mesh = _meshes[item.Geometry.Id];
                if (mesh.Source.Indices.Length > 0 && item.Color.A >= .999f) _nextCasters.Add(item.Geometry.Id);
                _draws.Add(new(item, mesh, checked((uint)surfaceSlot * _objectStride), checked((uint)lineSlot * _objectStride)));
                _hasTransparency |= scene.Mode != DisplayMode.Wireframe && mesh.Source.Indices.Length > 0
                    && item.Color.A > 0 && (scene.Mode == DisplayMode.Ghosted || item.Color.A < .999f);
            }
            Write(_objectsBuffer, _objectData.AsSpan(0, checked((scene.Items.Count * 2 + 1) * (int)_objectStride / 4)));
            MaterialUploadCount++;
            if (!_casters.SetEquals(_nextCasters)) _shadowValid = false;
            (_casters, _nextCasters) = (_nextCasters, _casters);
            _itemRevision = scene.Revision; _itemMode = scene.Mode;
            _itemMetallic = settings.Metallic; _itemRoughness = settings.Roughness;
        }
        if (sceneChanged) _shadowValid = false;
        _lastScene = scene;
    }
    private void SetObject(int slot, RenderColor color, float width, WebGpuRenderSettings settings)
    {
        var values = _objectData.AsSpan(checked(slot * (int)_objectStride / 4), 8);
        values[0] = color.R; values[1] = color.G; values[2] = color.B; values[3] = color.A;
        values[4] = settings.Metallic; values[5] = settings.Roughness; values[6] = width; values[7] = 0;
    }

    /// <summary>Encode one complete frame. TargetView is borrowed and is never retained beyond this call.</summary>
    public void Render(DisplayScene scene, IReadOnlyList<ViewportRectangle> viewports, TextureView* targetView, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene); ArgumentNullException.ThrowIfNull(viewports);
        if (targetView == null) throw new ArgumentException("A valid Uno target view is required.");
        if (viewports.Count > 4) throw new ArgumentException("This CAD target supports at most four views.");
        if (width < 1 || height < 1) return;
        var begin = Stopwatch.GetTimestamp();
        var settings = Settings ?? throw new InvalidOperationException("Rendering settings are required."); settings.Validate();
        Synchronize(scene, settings); Resize(width, height);
        TriangleCount = LineCount = DrawCalls = CulledItems = 0;
        var light = settings.LightDirection.Normalized();
        var shadowEnabled = settings.Shadows && scene.Mode == DisplayMode.Shaded;
        var shadowMatrix = WebGpuRenderMath.ShadowMatrix(scene.Bounds, scene.Origin, light, ShadowSize);
        PrepareViews(scene, viewports, shadowMatrix, settings, light);
        Span<float> post = stackalloc float[8]; post.Clear();
        post[0] = settings.Exposure; post[1] = settings.BloomStrength;
        post[2] = _targetFormat is TextureFormat.Rgba8UnormSrgb or TextureFormat.Bgra8UnormSrgb ? 0 : 1;
        SetPostLayout(post, viewports, 3);
        Write(_compositeParameters, post);
        post.Clear(); post[0] = 1.15f; post[1] = .55f; SetPostLayout(post, viewports, 2); Write(_bloomParameters, post);
        var encoder = Require(_api.DeviceCreateCommandEncoder(_device, null), "frame command encoder");
        try
        {
            // Material/selection/exposure changes do not alter a depth-only map.
            // While disabled, retain the neutral map without redrawing it for light edits.
            if (!_shadowValid || _shadowEnabled != shadowEnabled || (shadowEnabled
                && (_shadowRevision != scene.MeshRevision || _shadowClip != scene.ClipPlane || _shadowLight != light)))
            {
                RenderShadow(encoder, scene, shadowEnabled);
                _shadowRevision = scene.MeshRevision; _shadowClip = scene.ClipPlane; _shadowLight = light;
                _shadowEnabled = shadowEnabled; _shadowValid = true;
            }
            RenderOpaque(encoder, scene);
            RenderTransparency(encoder, scene);
            if (settings.BloomStrength > 0) RenderBloom(encoder);
            Composite(encoder, targetView);
            Submit(encoder); Frames++;
        }
        finally { _api.CommandEncoderRelease(encoder); LastSubmissionMilliseconds = Stopwatch.GetElapsedTime(begin).TotalMilliseconds; }
    }
    private void SetPostLayout(Span<float> data, IReadOnlyList<ViewportRectangle> views, int flag)
    {
        if (views.Count != 4) return;
        // Match ViewportLayout's asymmetric split exactly, including odd target sizes.
        data[flag] = 1;
        data[4] = views[0].Width; data[5] = views[0].Height;
        data[6] = views[1].X - views[0].Width; data[7] = views[2].Y - views[0].Height;
    }

    private void PrepareViews(DisplayScene scene, IReadOnlyList<ViewportRectangle> views, Transform3 shadowMatrix, WebGpuRenderSettings settings, Vec3 light)
    {
        if (_frameData.Length == 0) _frameData = new float[checked(5 * (int)_frameStride / 4)];
        _views.Clear();
        for (var i = 0; i < views.Count; i++)
        {
            var view = views[i]; if (view.Width <= 0 || view.Height <= 0) continue;
            if (view.X < 0 || view.Y < 0 || (long)view.X + view.Width > _width || (long)view.Y + view.Height > _height)
                throw new ArgumentException("A viewport extends outside its render target.");
            var matrix = WebGpuRenderMath.ReverseZCameraMatrix(view.Camera, view.Width, view.Height, scene.Origin);
            SetFrame(i, matrix, shadowMatrix, view.Camera.Basis(scene.Origin).Eye, scene, settings, light, view.Width, view.Height);
            var visible = _visible[i]; visible.Clear();
            foreach (var draw in _draws)
                // A curve's centerline may be outside while its screen-space ribbon
                // still covers edge pixels. Preserve pure curves for GPU clipping.
                if (draw.Mesh.Source.Indices.Length == 0 || WebGpuRenderMath.IsVisible(draw.Mesh.Source.Bounds, matrix, scene.Origin)) visible.Add(draw); else CulledItems++;
            _views.Add(new(view, checked((uint)i * _frameStride), visible));
            if (scene.ShowGrid && !_grids.ContainsKey(view.Name)) _grids.Add(view.Name, UploadLines(MeshBuilder.Grid(scene.Origin, view.Name)));
        }
        SetFrame(4, shadowMatrix, shadowMatrix, Vec3.Zero, scene, settings, light, ShadowSize, ShadowSize);
        Write(_framesBuffer, _frameData);
    }
    private void SetFrame(int slot, Transform3 matrix, Transform3 shadow, Vec3 eye, DisplayScene scene, WebGpuRenderSettings settings, Vec3 light, int width, int height)
    {
        var data = _frameData.AsSpan(checked(slot * (int)_frameStride / 4), 64); data.Clear();
        WebGpuRenderMath.WriteMatrix(matrix, data[..16]); WebGpuRenderMath.WriteMatrix(shadow, data.Slice(16, 16));
        data[32] = (float)eye.X; data[33] = (float)eye.Y; data[34] = (float)eye.Z; data[35] = settings.Exposure;
        if (scene.ClipPlane is { } clip)
        {
            data[36] = (float)clip.Normal.X; data[37] = (float)clip.Normal.Y; data[38] = (float)clip.Normal.Z;
            data[39] = (float)(clip.Offset + Vec3.Dot(clip.Normal, scene.Origin)); data[48] = 1;
        }
        data[40] = width; data[41] = height; data[42] = 1f / width; data[43] = 1f / height;
        data[44] = (float)light.X; data[45] = (float)light.Y; data[46] = (float)light.Z; data[47] = settings.Shadows ? 1 : 0;
        data[49] = settings.AmbientIntensity; data[50] = settings.LightIntensity;
    }

    private static RenderPassColorAttachment Attachment(TextureView* view, TextureView* resolve, Color clear) => new()
    {
        View = view, ResolveTarget = resolve, DepthSlice = uint.MaxValue,
        LoadOp = LoadOp.Clear, StoreOp = resolve == null ? StoreOp.Store : StoreOp.Discard, ClearValue = clear
    };
    private void RenderShadow(CommandEncoder* encoder, DisplayScene scene, bool enabled)
    {
        var depth = new RenderPassDepthStencilAttachment { View = _shadow!.View, DepthLoadOp = LoadOp.Clear, DepthStoreOp = StoreOp.Store, DepthClearValue = 1 };
        var descriptor = new RenderPassDescriptor { DepthStencilAttachment = &depth };
        var pass = Require(_api.CommandEncoderBeginRenderPass(encoder, &descriptor), "shadow pass");
        ShadowPassCount++;
        try
        {
            if (!enabled || scene.Mode != DisplayMode.Shaded) return;
            _api.RenderPassEncoderSetPipeline(pass, _shadowPipeline);
            var offset = 4 * _frameStride; _api.RenderPassEncoderSetBindGroup(pass, 0, _shadowGroup, 1, &offset);
            foreach (var draw in _draws)
                if (draw.Mesh.Source.Indices.Length > 0 && draw.Item.Color.A >= .999f)
                { DrawMesh(pass, draw.Mesh, false); ShadowCasterDrawCount++; }
        }
        finally { _api.RenderPassEncoderEnd(pass); _api.RenderPassEncoderRelease(pass); }
    }
    private void SetView(RenderPassEncoder* pass, PreparedView prepared)
    {
        var v = prepared.View;
        _api.RenderPassEncoderSetViewport(pass, v.X, v.Y, v.Width, v.Height, 0, 1);
        _api.RenderPassEncoderSetScissorRect(pass, (uint)v.X, (uint)v.Y, (uint)v.Width, (uint)v.Height);
        var offset = prepared.FrameOffset; _api.RenderPassEncoderSetBindGroup(pass, 0, _frameGroup, 1, &offset);
    }
    private void Object(RenderPassEncoder* pass, uint offset) => _api.RenderPassEncoderSetBindGroup(pass, 1, _objectGroup, 1, &offset);
    private void DrawMesh(RenderPassEncoder* pass, GpuMesh mesh, bool count = true)
    {
        _api.RenderPassEncoderSetVertexBuffer(pass, 0, mesh.Vertices, 0, (ulong)mesh.Source.Vertices.Length * 4);
        _api.RenderPassEncoderSetIndexBuffer(pass, mesh.Indices, IndexFormat.Uint32, 0, (ulong)mesh.Source.Indices.Length * 4);
        _api.RenderPassEncoderDrawIndexed(pass, (uint)mesh.Source.Indices.Length, 1, 0, 0, 0);
        DrawCalls++; if (count) TriangleCount += mesh.Source.Indices.Length / 3;
    }
    private void DrawLines(RenderPassEncoder* pass, GpuLines lines)
    {
        if (lines.Count == 0) return;
        _api.RenderPassEncoderSetVertexBuffer(pass, 0, lines.Buffer, 0, (ulong)lines.Count * 24);
        _api.RenderPassEncoderDraw(pass, 6, lines.Count, 0, 0); DrawCalls++; LineCount += (int)lines.Count;
    }
    private void RenderOpaque(CommandEncoder* encoder, DisplayScene scene)
    {
        var color = Attachment(_hdrMultisample!.View, _hdr!.View, new(.67, .71, .75, 1));
        var depth = new RenderPassDepthStencilAttachment { View = _depth!.View, DepthLoadOp = LoadOp.Clear, DepthStoreOp = StoreOp.Store, DepthClearValue = 0 };
        var descriptor = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &color, DepthStencilAttachment = &depth };
        var pass = Require(_api.CommandEncoderBeginRenderPass(encoder, &descriptor), "HDR opaque pass");
        try
        {
            foreach (var prepared in _views)
            {
                SetView(pass, prepared);
                if (scene.Mode == DisplayMode.Shaded)
                {
                    _api.RenderPassEncoderSetPipeline(pass, _surfacePipeline);
                    foreach (var draw in prepared.Draws)
                        if (draw.Item.Color.A >= .999f && draw.Mesh.Source.Indices.Length > 0) { Object(pass, draw.SurfaceOffset); DrawMesh(pass, draw.Mesh); }
                }
                _api.RenderPassEncoderSetPipeline(pass, _linePipeline);
                if (scene.ShowGrid) { Object(pass, 0); DrawLines(pass, _grids[prepared.View.Name]); }
                foreach (var draw in prepared.Draws)
                    if (scene.ShowIsocurves || draw.Mesh.Source.Indices.Length == 0 || scene.Mode == DisplayMode.Wireframe || draw.Item.Selected)
                    { Object(pass, draw.LineOffset); DrawLines(pass, draw.Mesh.Lines); }
            }
        }
        finally { _api.RenderPassEncoderEnd(pass); _api.RenderPassEncoderRelease(pass); }
    }
    private void RenderTransparency(CommandEncoder* encoder, DisplayScene scene)
    {
        var colors = stackalloc RenderPassColorAttachment[2];
        colors[0] = Attachment(_accumMultisample!.View, _accum!.View, new(0, 0, 0, 0));
        colors[1] = Attachment(_revealMultisample!.View, _reveal!.View, new(1, 1, 1, 1));
        var depth = new RenderPassDepthStencilAttachment { View = _depth!.View, DepthReadOnly = true };
        // Opaque-only scenes use cleared 1x1 OIT targets; attaching the full-size
        // depth texture would violate WebGPU render-pass attachment dimensions.
        var descriptor = new RenderPassDescriptor { ColorAttachmentCount = 2, ColorAttachments = colors, DepthStencilAttachment = _hasTransparency ? &depth : null };
        var pass = Require(_api.CommandEncoderBeginRenderPass(encoder, &descriptor), "weighted transparency pass");
        try
        {
            if (!_hasTransparency) return;
            _api.RenderPassEncoderSetPipeline(pass, _ghostPipeline);
            foreach (var prepared in _views)
            {
                SetView(pass, prepared);
                foreach (var draw in prepared.Draws)
                    if ((scene.Mode == DisplayMode.Ghosted || draw.Item.Color.A < .999f) && draw.Item.Color.A > 0 && draw.Mesh.Source.Indices.Length > 0)
                    { Object(pass, draw.SurfaceOffset); DrawMesh(pass, draw.Mesh); }
            }
        }
        finally { _api.RenderPassEncoderEnd(pass); _api.RenderPassEncoderRelease(pass); }
    }
    private void RenderBloom(CommandEncoder* encoder)
    {
        Dispatch(_downsamplePipeline, _downsampleGroup); Dispatch(_horizontalPipeline, _horizontalGroup); Dispatch(_verticalPipeline, _verticalGroup);
        void Dispatch(ComputePipeline* pipeline, BindGroup* group)
        {
            var pass = Require(_api.CommandEncoderBeginComputePass(encoder, null), "bloom compute pass");
            try
            {
                _api.ComputePassEncoderSetPipeline(pass, pipeline); _api.ComputePassEncoderSetBindGroup(pass, 0, group, 0, null);
                _api.ComputePassEncoderDispatchWorkgroups(pass, (uint)(_width + 15) / 16, (uint)(_height + 15) / 16, 1);
            }
            finally { _api.ComputePassEncoderEnd(pass); _api.ComputePassEncoderRelease(pass); }
        }
    }
    private void Composite(CommandEncoder* encoder, TextureView* target)
    {
        var color = Attachment(target, null, new(0, 0, 0, 1));
        var descriptor = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &color };
        var pass = Require(_api.CommandEncoderBeginRenderPass(encoder, &descriptor), "HDR composition pass");
        try
        {
            _api.RenderPassEncoderSetPipeline(pass, _compositePipeline);
            _api.RenderPassEncoderSetBindGroup(pass, 0, _compositeGroup, 0, null);
            _api.RenderPassEncoderDraw(pass, 3, 1, 0, 0); DrawCalls++;
        }
        finally { _api.RenderPassEncoderEnd(pass); _api.RenderPassEncoderRelease(pass); }
    }
    private void Submit(CommandEncoder* encoder)
    {
        var commands = Require(_api.CommandEncoderFinish(encoder, null), "command buffer");
        try { _api.QueueSubmit(_queue, 1, &commands); }
        finally { _api.CommandBufferRelease(commands); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _captureRequest?.Cancel(); _captureRequest = null;
        foreach (var mesh in _meshes.Values) Delete(mesh); _meshes.Clear();
        foreach (var grid in _grids.Values) Release(ref grid.Buffer); _grids.Clear();
        ReleaseTargets(); Release(ref _frameGroup); Release(ref _shadowGroup); Release(ref _objectGroup);
        Release(ref _framesBuffer); Release(ref _objectsBuffer); Release(ref _bloomParameters); Release(ref _compositeParameters);
        _shadow?.Dispose(); _shadow = null;
        if (_shadowSampler != null) _api.SamplerRelease(_shadowSampler); _shadowSampler = null;
        if (_linearSampler != null) _api.SamplerRelease(_linearSampler); _linearSampler = null;
        foreach (var pipeline in _renderPipelines) _api.RenderPipelineRelease((RenderPipeline*)pipeline); _renderPipelines.Clear();
        foreach (var pipeline in _computePipelines) _api.ComputePipelineRelease((ComputePipeline*)pipeline); _computePipelines.Clear();
        foreach (var layout in _pipelineLayouts) _api.PipelineLayoutRelease((PipelineLayout*)layout); _pipelineLayouts.Clear();
        foreach (var layout in _layouts) _api.BindGroupLayoutRelease((BindGroupLayout*)layout); _layouts.Clear();
    }
}
