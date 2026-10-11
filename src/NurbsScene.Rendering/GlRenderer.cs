using System.Reflection;
using NurbsScene.Geometry;
using Silk.NET.OpenGL;

namespace NurbsScene.Rendering;

/// <summary>
/// OpenGL 3.3 / OpenGL ES 3 renderer, independent of any windowing toolkit.
/// Owns its color/depth attachments, never assumes the host framebuffer has depth,
/// and restores the GL state it changes. Dispose only with its context current.
/// </summary>
public sealed unsafe class GlRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly bool _es;
    private readonly ProgramState _surface, _line, _compose;
    private readonly Dictionary<Guid, GpuMesh> _meshes = [];
    private readonly Dictionary<string, GpuLines> _grids = [];
    private Vec3 _origin = new(double.NaN, 0, 0);
    private uint _framebuffer, _colorTexture, _depthBuffer, _composeVao;
    private int _width, _height;
    private bool _disposed;
    public long Frames { get; private set; }
    public string Backend { get; }
    public int TriangleCount { get; private set; }
    public int LineCount { get; private set; }

    private sealed record ProgramState(uint Id, int Matrix, int Eye, int Color, int Material, int Viewport, int ClipPlane);
    private sealed record GpuLines(uint Vao, uint Buffer, int Count);
    private sealed record GpuMesh(DisplayMesh Source, uint Vao, uint Vertices, uint Indices, GpuLines Lines);

    public GlRenderer(GL gl)
    {
        _gl = gl;
        _es = gl.GetStringS(StringName.ShadingLanguageVersion).Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);
        Backend = $"{gl.GetStringS(StringName.Version)} / {gl.GetStringS(StringName.Renderer)}";
        using var state = new HostState(gl, _es);
        try
        {
            _surface = Link(Resource("vertex.glsl"), Resource("fragment.glsl"));
            _line = Link(Resource("lineVertex.glsl"), Resource("lineFragment.glsl"));
            _compose = Link("#version 300 es\nprecision highp float;out vec2 uv;void main(){vec2 p=vec2(float((gl_VertexID<<1)&2),float(gl_VertexID&2));uv=p;gl_Position=vec4(p*2.-1.,0.,1.);}",
                "#version 300 es\nprecision highp float;in vec2 uv;uniform sampler2D scene;out vec4 pixel;void main(){pixel=texture(scene,uv);}");
            _composeVao = gl.GenVertexArray();
        }
        catch
        {
            if (_surface is not null) gl.DeleteProgram(_surface.Id);
            if (_line is not null) gl.DeleteProgram(_line.Id);
            if (_compose is not null) gl.DeleteProgram(_compose.Id);
            throw;
        }
    }

    private static string Resource(string name)
    {
        var assembly = typeof(GlRenderer).Assembly;
        using var stream = assembly.GetManifestResourceStream($"NurbsScene.Rendering.Shaders.{name}")
            ?? throw new InvalidOperationException($"Missing renderer shader {name}.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private uint Compile(ShaderType kind, string source)
    {
        var shader = _gl.CreateShader(kind);
        try
        {
            _gl.ShaderSource(shader, _es ? source : source.Replace("#version 300 es", "#version 330 core"));
            _gl.CompileShader(shader); _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
            if (success == 0) throw new InvalidOperationException($"{kind}: {_gl.GetShaderInfoLog(shader)}");
            return shader;
        }
        catch { _gl.DeleteShader(shader); throw; }
    }
    private ProgramState Link(string vertex, string fragment)
    {
        var v = Compile(ShaderType.VertexShader, vertex); uint f = 0, program = 0;
        try
        {
            f = Compile(ShaderType.FragmentShader, fragment); program = _gl.CreateProgram();
            _gl.AttachShader(program, v); _gl.AttachShader(program, f); _gl.LinkProgram(program);
            _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int success);
            if (success == 0) throw new InvalidOperationException(_gl.GetProgramInfoLog(program));
            int Location(string name) => _gl.GetUniformLocation(program, name);
            return new(program, Location("matrix"), Location("eye"), Location("color"), Location("material"), Location("viewport"), Location("clipPlane"));
        }
        catch { if (program != 0) _gl.DeleteProgram(program); throw; }
        finally { _gl.DeleteShader(v); if (f != 0) _gl.DeleteShader(f); }
    }
    private void Resize(int width, int height)
    {
        if (_width == width && _height == height) return;
        var maximum = _gl.GetInteger(GetPName.MaxTextureSize);
        if (width < 1 || height < 1 || width > maximum || height > maximum) throw new ArgumentOutOfRangeException(nameof(width), "Viewport exceeds the GPU texture limit.");
        ReleaseTarget();
        _framebuffer = _gl.GenFramebuffer(); _colorTexture = _gl.GenTexture(); _depthBuffer = _gl.GenRenderbuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.ActiveTexture(TextureUnit.Texture0); _gl.BindTexture(TextureTarget.Texture2D, _colorTexture);
        _gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, 0);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _colorTexture, 0);
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)width, (uint)height);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depthBuffer);
        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete) { ReleaseTarget(); throw new InvalidOperationException("A depth-enabled CAD framebuffer could not be created."); }
        _width = width; _height = height;
    }
    private GpuLines UploadLines(float[] values)
    {
        var vao = _gl.GenVertexArray(); var buffer = _gl.GenBuffer();
        try
        {
            _gl.BindVertexArray(vao); _gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, new ReadOnlySpan<float>(values), BufferUsageARB.StaticDraw);
            for (uint a = 0; a < 2; a++) { _gl.EnableVertexAttribArray(a); _gl.VertexAttribPointer(a, 3, GLEnum.Float, false, 24, (void*)(a * 12)); _gl.VertexAttribDivisor(a, 1); }
            return new(vao, buffer, values.Length / 6);
        }
        catch { _gl.DeleteVertexArray(vao); _gl.DeleteBuffer(buffer); throw; }
    }
    private GpuMesh Upload(DisplayMesh source)
    {
        var vao = _gl.GenVertexArray(); var vertices = _gl.GenBuffer(); var indices = _gl.GenBuffer();
        GpuLines? lines = null;
        try
        {
            _gl.BindVertexArray(vao); _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertices);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, new ReadOnlySpan<float>(source.Vertices), BufferUsageARB.StaticDraw);
            for (uint a = 0; a < 2; a++) { _gl.EnableVertexAttribArray(a); _gl.VertexAttribPointer(a, 3, GLEnum.Float, false, 24, (void*)(a * 12)); }
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, indices);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, new ReadOnlySpan<uint>(source.Indices), BufferUsageARB.StaticDraw);
            lines = UploadLines(source.Lines); return new(source, vao, vertices, indices, lines);
        }
        catch { _gl.DeleteVertexArray(vao); _gl.DeleteBuffer(vertices); _gl.DeleteBuffer(indices); if (lines is not null) Delete(lines); throw; }
    }
    private void Synchronize(DisplayScene scene)
    {
        foreach (var id in _meshes.Keys.ToArray()) if (!scene.Meshes.TryGetValue(id, out var source) || !ReferenceEquals(source, _meshes[id].Source)) { Delete(_meshes[id]); _meshes.Remove(id); }
        foreach (var (id, source) in scene.Meshes) if (!_meshes.ContainsKey(id)) _meshes.Add(id, Upload(source));
        if (_origin != scene.Origin) { foreach (var grid in _grids.Values) Delete(grid); _grids.Clear(); _origin = scene.Origin; }
    }
    private void Uniforms(ProgramState program, DisplayScene scene, ViewportRectangle viewport, RenderColor color, float width = 1.2f)
    {
        _gl.UseProgram(program.Id);
        var matrix = viewport.Camera.Matrix(viewport.Width, viewport.Height, scene.Origin).Values.ToArray().Select(v => (float)v).ToArray();
        fixed (float* values = matrix) _gl.UniformMatrix4(program.Matrix, 1, false, values);
        var eye = viewport.Camera.Basis().Eye - scene.Origin;
        _gl.Uniform3(program.Eye, (float)eye.X, (float)eye.Y, (float)eye.Z);
        _gl.Uniform4(program.Color, color.R, color.G, color.B, color.A);
        _gl.Uniform4(program.Material, .12f, .42f, 0f, scene.ClipPlane.HasValue ? 1f : 0f);
        _gl.Uniform3(program.Viewport, (float)viewport.Width, (float)viewport.Height, width);
        var (normal, offset) = scene.ClipPlane ?? (Vec3.Zero, 0d);
        _gl.Uniform4(program.ClipPlane, (float)normal.X, (float)normal.Y, (float)normal.Z, (float)(offset + Vec3.Dot(normal, scene.Origin)));
    }
    private void DrawLines(GpuLines lines) { if (lines.Count == 0) return; _gl.BindVertexArray(lines.Vao); _gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, (uint)lines.Count); LineCount += lines.Count; }
    public void Render(DisplayScene scene, IReadOnlyList<ViewportRectangle> viewports, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width < 1 || height < 1) return;
        using var host = new HostState(_gl, _es);
        Resize(width, height); Synchronize(scene); TriangleCount = 0; LineCount = 0;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Disable(EnableCap.StencilTest); _gl.Disable(EnableCap.CullFace); _gl.Disable(EnableCap.RasterizerDiscard); _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.ColorMask(true, true, true, true); _gl.DepthMask(true); _gl.Enable(EnableCap.DepthTest); _gl.DepthFunc(DepthFunction.Lequal);
        if (_es) _gl.ClearDepth(1f); else _gl.ClearDepth(1d);
        _gl.Disable(EnableCap.ScissorTest); _gl.ClearColor(.57f, .58f, .58f, 1f); _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.ScissorTest); _gl.Enable(EnableCap.Blend);
        _gl.BlendEquationSeparate(BlendEquationModeEXT.FuncAdd, BlendEquationModeEXT.FuncAdd);
        _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        foreach (var view in viewports.Where(v => v.Width > 0 && v.Height > 0))
        {
            _gl.Viewport(view.X, height - view.Y - view.Height, (uint)view.Width, (uint)view.Height);
            _gl.Scissor(view.X, height - view.Y - view.Height, (uint)view.Width, (uint)view.Height);
            _gl.DepthMask(true); _gl.ClearColor(.91f, .92f, .92f, 1f); _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            if (scene.ShowGrid)
            {
                if (!_grids.TryGetValue(view.Name, out var grid)) _grids.Add(view.Name, grid = UploadLines(MeshBuilder.Grid(scene.Origin, view.Name)));
                Uniforms(_line, scene, view, new(.59f, .61f, .61f, .48f), .8f); DrawLines(grid);
            }
            var items = scene.Mode == DisplayMode.Ghosted ? scene.Items.OrderByDescending(i => (i.Geometry.Bounds.Center - view.Camera.Basis().Eye).Length).ToArray() : scene.Items;
            foreach (var item in items)
            {
                var mesh = _meshes[item.Geometry.Id]; var color = item.Selected ? new RenderColor(.96f, .68f, .12f) : item.Color;
                if (scene.Mode != DisplayMode.Wireframe && mesh.Source.Indices.Length > 0)
                {
                    if (scene.Mode == DisplayMode.Ghosted) { color = color with { A = .35f }; _gl.DepthMask(false); }
                    Uniforms(_surface, scene, view, color); _gl.BindVertexArray(mesh.Vao);
                    _gl.DrawElements(PrimitiveType.Triangles, (uint)mesh.Source.Indices.Length, DrawElementsType.UnsignedInt, null); TriangleCount += mesh.Source.Indices.Length / 3;
                    _gl.DepthMask(true);
                }
                if (scene.ShowIsocurves || mesh.Source.Indices.Length == 0 || scene.Mode == DisplayMode.Wireframe || item.Selected)
                {
                    Uniforms(_line, scene, view, item.Selected ? new(1f, .62f, .03f) : new(.13f, .27f, .27f, .78f), item.Selected ? 2f : 1.15f);
                    // Transparent display is approximate depth-sorted alpha, not order-independent transparency.
                    _gl.DepthMask(scene.Mode != DisplayMode.Ghosted); DrawLines(mesh.Lines); _gl.DepthMask(true);
                }
            }
        }
        // Composite rather than blit: works when the host target is multisampled too.
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)host.DrawFramebuffer);
        _gl.Viewport(host.Viewport[0], host.Viewport[1], (uint)host.Viewport[2], (uint)host.Viewport[3]);
        _gl.Scissor(host.Viewport[0], host.Viewport[1], (uint)host.Viewport[2], (uint)host.Viewport[3]);
        _gl.Disable(EnableCap.DepthTest); _gl.Disable(EnableCap.Blend); _gl.UseProgram(_compose.Id);
        _gl.ActiveTexture(TextureUnit.Texture0); _gl.BindTexture(TextureTarget.Texture2D, _colorTexture);
        _gl.BindSampler(0, 0);
        _gl.Uniform1(_gl.GetUniformLocation(_compose.Id, "scene"), 0); _gl.BindVertexArray(_composeVao); _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        var error = _gl.GetError(); if (error != GLEnum.NoError) throw new InvalidOperationException($"GPU submission failed: {error}.");
        Frames++;
    }
    public byte[] CaptureRgba()
    {
        if (_framebuffer == 0) throw new InvalidOperationException("No completed render target.");
        using var host = new HostState(_gl, _es);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _framebuffer);
        _gl.BindBuffer(BufferTargetARB.PixelPackBuffer, 0);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1); _gl.PixelStore(PixelStoreParameter.PackRowLength, 0); _gl.PixelStore(PixelStoreParameter.PackSkipPixels, 0); _gl.PixelStore(PixelStoreParameter.PackSkipRows, 0);
        var bytes = new byte[checked(_width * _height * 4)]; fixed (byte* p = bytes) _gl.ReadPixels(0, 0, (uint)_width, (uint)_height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        return bytes; // Bottom-up RGBA8; callers must preserve/report this convention.
    }
    private void Delete(GpuLines lines) { _gl.DeleteVertexArray(lines.Vao); _gl.DeleteBuffer(lines.Buffer); }
    private void Delete(GpuMesh mesh) { _gl.DeleteVertexArray(mesh.Vao); _gl.DeleteBuffer(mesh.Vertices); _gl.DeleteBuffer(mesh.Indices); Delete(mesh.Lines); }
    private void ReleaseTarget() { if (_framebuffer != 0) _gl.DeleteFramebuffer(_framebuffer); if (_colorTexture != 0) _gl.DeleteTexture(_colorTexture); if (_depthBuffer != 0) _gl.DeleteRenderbuffer(_depthBuffer); _framebuffer = _colorTexture = _depthBuffer = 0; _width = _height = 0; }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var mesh in _meshes.Values) Delete(mesh); foreach (var grid in _grids.Values) Delete(grid); _meshes.Clear(); _grids.Clear(); ReleaseTarget();
        _gl.DeleteVertexArray(_composeVao); _gl.DeleteProgram(_surface.Id); _gl.DeleteProgram(_line.Id); _gl.DeleteProgram(_compose.Id);
    }
    private sealed class HostState : IDisposable
    {
        private readonly GL _gl; private readonly bool _es;
        private readonly Dictionary<GetPName, int> _integers = [];
        private readonly Dictionary<EnableCap, bool> _enabled = [];
        private readonly float[] _clear = new float[4]; private readonly int[] _colorMask = new int[4]; private readonly int[] _scissor = new int[4];
        private readonly float _clearDepth; private readonly int _texture0, _sampler0;
        public int[] Viewport { get; } = new int[4];
        public int DrawFramebuffer => _integers[GetPName.DrawFramebufferBinding];
        public HostState(GL gl, bool es)
        {
            _gl = gl; _es = es;
            foreach (var p in new[] { GetPName.CurrentProgram, GetPName.VertexArrayBinding, GetPName.ArrayBufferBinding, GetPName.ReadFramebufferBinding, GetPName.DrawFramebufferBinding, GetPName.RenderbufferBinding, GetPName.DepthWritemask, GetPName.DepthFunc, GetPName.BlendSrcRgb, GetPName.BlendDstRgb, GetPName.BlendSrcAlpha, GetPName.BlendDstAlpha, GetPName.BlendEquationRgb, GetPName.BlendEquationAlpha, GetPName.ActiveTexture, GetPName.PixelPackBufferBinding, GetPName.PixelUnpackBufferBinding, GetPName.PackAlignment, GetPName.PackRowLength, GetPName.PackSkipPixels, GetPName.PackSkipRows }) _integers[p] = gl.GetInteger(p);
            foreach (var cap in new[] { EnableCap.DepthTest, EnableCap.ScissorTest, EnableCap.Blend, EnableCap.CullFace, EnableCap.StencilTest, EnableCap.RasterizerDiscard, EnableCap.PolygonOffsetFill }) _enabled[cap] = gl.IsEnabled(cap);
            fixed (int* p = Viewport) gl.GetInteger(GetPName.Viewport, p); fixed (int* p = _scissor) gl.GetInteger(GetPName.ScissorBox, p); fixed (int* p = _colorMask) gl.GetInteger(GetPName.ColorWritemask, p); fixed (float* p = _clear) gl.GetFloat(GetPName.ColorClearValue, p);
            _clearDepth = gl.GetFloat(GetPName.DepthClearValue); gl.ActiveTexture(TextureUnit.Texture0); _texture0 = gl.GetInteger(GetPName.TextureBinding2D); _sampler0 = gl.GetInteger(GetPName.SamplerBinding);
        }
        public void Dispose()
        {
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)_integers[GetPName.ReadFramebufferBinding]); _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)DrawFramebuffer);
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, (uint)_integers[GetPName.RenderbufferBinding]); _gl.BindVertexArray((uint)_integers[GetPName.VertexArrayBinding]); _gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)_integers[GetPName.ArrayBufferBinding]); _gl.UseProgram((uint)_integers[GetPName.CurrentProgram]);
            _gl.ActiveTexture(TextureUnit.Texture0); _gl.BindTexture(TextureTarget.Texture2D, (uint)_texture0); _gl.BindSampler(0, (uint)_sampler0); _gl.ActiveTexture((TextureUnit)_integers[GetPName.ActiveTexture]);
            _gl.BindBuffer(BufferTargetARB.PixelPackBuffer, (uint)_integers[GetPName.PixelPackBufferBinding]); _gl.BindBuffer(BufferTargetARB.PixelUnpackBuffer, (uint)_integers[GetPName.PixelUnpackBufferBinding]);
            foreach (var name in new[] {GetPName.PackAlignment, GetPName.PackRowLength, GetPName.PackSkipPixels, GetPName.PackSkipRows}) _gl.PixelStore((PixelStoreParameter)name, _integers[name]);
            _gl.Viewport(Viewport[0], Viewport[1], (uint)Viewport[2], (uint)Viewport[3]); _gl.Scissor(_scissor[0], _scissor[1], (uint)_scissor[2], (uint)_scissor[3]);
            _gl.DepthFunc((DepthFunction)_integers[GetPName.DepthFunc]); _gl.DepthMask(_integers[GetPName.DepthWritemask] != 0); _gl.ColorMask(_colorMask[0] != 0, _colorMask[1] != 0, _colorMask[2] != 0, _colorMask[3] != 0);
            _gl.ClearColor(_clear[0], _clear[1], _clear[2], _clear[3]); if (_es) _gl.ClearDepth(_clearDepth); else _gl.ClearDepth((double)_clearDepth);
            _gl.BlendEquationSeparate((BlendEquationModeEXT)_integers[GetPName.BlendEquationRgb], (BlendEquationModeEXT)_integers[GetPName.BlendEquationAlpha]);
            _gl.BlendFuncSeparate((BlendingFactor)_integers[GetPName.BlendSrcRgb], (BlendingFactor)_integers[GetPName.BlendDstRgb], (BlendingFactor)_integers[GetPName.BlendSrcAlpha], (BlendingFactor)_integers[GetPName.BlendDstAlpha]);
            foreach (var (cap, enabled) in _enabled) if (enabled) _gl.Enable(cap); else _gl.Disable(cap);
        }
    }
}
