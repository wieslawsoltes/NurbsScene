"""Explicit native WebGPU shader/pass qualification, independent of Uno hosts.

Install tests/gpu/requirements.txt and run this script. A software adapter is
requested deliberately and is recorded in the report. These are correctness
checks, never hardware throughput measurements or a substitute for the Uno UI
browser campaigns.
"""
import argparse
import array
import json
from pathlib import Path

import wgpu

ROOT = Path(__file__).resolve().parents[2]


def qualify():
    adapter = wgpu.gpu.request_adapter_sync(force_fallback_adapter=True)
    device = adapter.request_device_sync()
    report = {"adapter": dict(adapter.info), "qualification": "native software-adapter correctness; not Uno integration or physical-GPU performance", "checks": []}
    owned = []

    def check(ok, name):
        if not ok:
            raise AssertionError(name)
        report["checks"].append(name)

    modules = {}
    for name in ("scene", "bloom", "composite"):
        modules[name] = device.create_shader_module(label=name, code=(ROOT / f"src/NurbsScene.Rendering/Shaders/{name}.wgsl").read_text())
        info = modules[name].get_compilation_info_sync()
        check(not any(item.type == "error" for item in info), f"{name} WGSL compiles")

    def buffer(values, usage):
        result = device.create_buffer_with_data(data=array.array("f", values), usage=usage | wgpu.BufferUsage.COPY_DST)
        owned.append(result)
        return result

    def texture(width, height, format, samples=1, usage=0):
        result = device.create_texture(size=(width, height, 1), format=format, sample_count=samples, usage=wgpu.TextureUsage.RENDER_ATTACHMENT | usage)
        owned.append(result)
        return result

    vf = wgpu.ShaderStage.VERTEX | wgpu.ShaderStage.FRAGMENT
    frame_values = [0.0] * 64
    for offset in (0, 16):
        for i in range(4):
            frame_values[offset + i * 5] = 1
    frame_values[32:36] = [0, 0, 3, 1]
    frame_values[44:48] = [-.3, -.5, .85, 0]
    frame_values[48:52] = [0, .55, 3.2, 0]
    frame = buffer(frame_values, wgpu.BufferUsage.UNIFORM)
    object_values = [0.0] * 256
    for slot, color in enumerate(([.85,.14,.08,1], [.07,.2,.9,1], [.85,.14,.08,.45], [.07,.2,.9,.35])):
        object_values[slot * 64:slot * 64 + 8] = color + [.12,.36,3,0]
    objects = buffer(object_values, wgpu.BufferUsage.UNIFORM)
    shadow = texture(256,256,"depth32float",usage=wgpu.TextureUsage.TEXTURE_BINDING)
    shadow_view = shadow.create_view()
    shadow_sampler = device.create_sampler(compare="less-equal", min_filter="linear", mag_filter="linear")
    frame_layout = device.create_bind_group_layout(entries=[
        {"binding":0,"visibility":vf,"buffer":{"type":"uniform","min_binding_size":256}},
        {"binding":1,"visibility":wgpu.ShaderStage.FRAGMENT,"texture":{"sample_type":"depth"}},
        {"binding":2,"visibility":wgpu.ShaderStage.FRAGMENT,"sampler":{"type":"comparison"}}])
    shadow_layout = device.create_bind_group_layout(entries=[{"binding":0,"visibility":vf,"buffer":{"type":"uniform","min_binding_size":256}}])
    object_layout = device.create_bind_group_layout(entries=[{"binding":0,"visibility":vf,"buffer":{"type":"uniform","has_dynamic_offset":True,"min_binding_size":32}}])
    frame_group = device.create_bind_group(layout=frame_layout,entries=[
        {"binding":0,"resource":{"buffer":frame,"size":256}}, {"binding":1,"resource":shadow_view}, {"binding":2,"resource":shadow_sampler}])
    shadow_group = device.create_bind_group(layout=shadow_layout,entries=[{"binding":0,"resource":{"buffer":frame,"size":256}}])
    object_group = device.create_bind_group(layout=object_layout,entries=[{"binding":0,"resource":{"buffer":objects,"size":32}}])
    scene_layout = device.create_pipeline_layout(bind_group_layouts=[frame_layout, object_layout])
    vertices = {"array_stride":24,"attributes":[{"shader_location":0,"format":"float32x3","offset":0},{"shader_location":1,"format":"float32x3","offset":12}]}

    def pipeline(fragment, targets, vertex="surfaceVertex", instance=False, depth_write=True):
        return device.create_render_pipeline(layout=scene_layout,
            vertex={"module":modules["scene"],"entry_point":vertex,"buffers":[{**vertices,"step_mode":"instance" if instance else "vertex"}]},
            fragment={"module":modules["scene"],"entry_point":fragment,"targets":targets},
            primitive={"topology":"triangle-list","cull_mode":"none"},
            depth_stencil={"format":"depth32float","depth_write_enabled":depth_write,"depth_compare":"greater-equal"},
            multisample={"count":4})

    surface_pipeline = pipeline("surfaceFragment",[{"format":"rgba16float"}])
    add = {"operation":"add","src_factor":"one","dst_factor":"one"}
    reveal_blend = {"operation":"add","src_factor":"zero","dst_factor":"one-minus-src-alpha"}
    ghost_pipeline = pipeline("ghostFragment",[
        {"format":"rgba16float","blend":{"color":add,"alpha":add}},
        {"format":"r8unorm","blend":{"color":reveal_blend,"alpha":reveal_blend}}],depth_write=False)
    line_pipeline = pipeline("lineFragment",[{"format":"rgba16float","blend":{
        "color":{"operation":"add","src_factor":"src-alpha","dst_factor":"one-minus-src-alpha"},
        "alpha":{"operation":"add","src_factor":"one","dst_factor":"one-minus-src-alpha"}}}],
        vertex="lineVertex",instance=True,depth_write=False)
    shadow_pipeline = device.create_render_pipeline(layout=device.create_pipeline_layout(bind_group_layouts=[shadow_layout]),
        vertex={"module":modules["scene"],"entry_point":"shadowVertex","buffers":[vertices]},
        fragment={"module":modules["scene"],"entry_point":"shadowFragment","targets":[]},
        primitive={"topology":"triangle-list","cull_mode":"none"},
        depth_stencil={"format":"depth32float","depth_write_enabled":True,"depth_compare":"less-equal"})
    compute = wgpu.ShaderStage.COMPUTE
    bloom_layout = device.create_bind_group_layout(entries=[
        {"binding":0,"visibility":compute,"texture":{"sample_type":"float"}},
        {"binding":1,"visibility":compute,"storage_texture":{"access":"write-only","format":"rgba16float"}},
        {"binding":2,"visibility":compute,"buffer":{"type":"uniform","min_binding_size":32}}])
    bloom_pipelines = {name:device.create_compute_pipeline(layout=device.create_pipeline_layout(bind_group_layouts=[bloom_layout]),
        compute={"module":modules["bloom"],"entry_point":name}) for name in ("downsample","blurHorizontal","blurVertical")}
    bloom_parameters = buffer([.3,.1,0,0,0,0,0,0],wgpu.BufferUsage.UNIFORM)
    post_parameters = buffer([1,0,1,0,0,0,0,0],wgpu.BufferUsage.UNIFORM)
    compose = device.create_render_pipeline(layout="auto",vertex={"module":modules["composite"],"entry_point":"fullscreenVertex"},
        fragment={"module":modules["composite"],"entry_point":"compositeFragment","targets":[{"format":"rgba8unorm"}]},primitive={"topology":"triangle-list"})
    linear_sampler = device.create_sampler(min_filter="linear",mag_filter="linear")
    def triangle(z):
        return buffer([-.85,-.8,z,0,0,1,.85,-.8,z,0,0,1,0,.85,z,0,0,1],wgpu.BufferUsage.VERTEX)
    near, far, caster = triangle(.7), triangle(.25), triangle(.2)
    ribbon = buffer([-.9,0,2,.9,0,.5],wgpu.BufferUsage.VERTEX)

    def harness(width,height):
        half_w,half_h = (width+1)//2,(height+1)//2
        sampled = wgpu.TextureUsage.TEXTURE_BINDING
        hdr=texture(width,height,"rgba16float",usage=sampled)
        hdr_ms=texture(width,height,"rgba16float",4)
        accum=texture(width,height,"rgba16float",usage=sampled)
        accum_ms=texture(width,height,"rgba16float",4)
        reveal=texture(width,height,"r8unorm",usage=sampled)
        reveal_ms=texture(width,height,"r8unorm",4)
        depth=texture(width,height,"depth32float",4)
        output=texture(width,height,"rgba8unorm",usage=wgpu.TextureUsage.COPY_SRC)
        bloom_a=device.create_texture(size=(half_w,half_h,1),format="rgba16float",usage=sampled|wgpu.TextureUsage.STORAGE_BINDING)
        bloom_b=device.create_texture(size=(half_w,half_h,1),format="rgba16float",usage=sampled|wgpu.TextureUsage.STORAGE_BINDING)
        owned.extend((bloom_a,bloom_b))
        hv,hmv,av,amv,rv,rmv,dv,ov,bav,bbv = [x.create_view() for x in (hdr,hdr_ms,accum,accum_ms,reveal,reveal_ms,depth,output,bloom_a,bloom_b)]
        def bloom_group(source,target):
            return device.create_bind_group(layout=bloom_layout,entries=[
                {"binding":0,"resource":source},{"binding":1,"resource":target},{"binding":2,"resource":{"buffer":bloom_parameters}}])
        bloom_groups=[bloom_group(hv,bav),bloom_group(bav,bbv),bloom_group(bbv,bav)]
        compose_group=device.create_bind_group(layout=compose.get_bind_group_layout(0),entries=[
            {"binding":0,"resource":hv},{"binding":1,"resource":av},{"binding":2,"resource":rv},
            {"binding":3,"resource":bav},{"binding":4,"resource":linear_sampler},{"binding":5,"resource":{"buffer":post_parameters}}])
        def render(opaque=(),ghost=(),shadowed=False,clip=False,line=False,bloom=0,quad=False):
            frame_values[40:44]=[width,height,1/width,1/height]
            frame_values[47]=1 if shadowed else 0
            frame_values[48]=1 if clip else 0
            frame_values[36:40]=[1,0,0,0]
            device.queue.write_buffer(frame,0,array.array("f",frame_values))
            partitions=[(width-2)//2,(height-2)//2,2,2] if quad else [0,0,0,0]
            device.queue.write_buffer(post_parameters,0,array.array("f",[1,bloom,1,int(quad)]+partitions))
            device.queue.write_buffer(bloom_parameters,0,array.array("f",[.3,.1,int(quad),0]+partitions))
            encoder=device.create_command_encoder()
            p=encoder.begin_render_pass(color_attachments=[],depth_stencil_attachment={"view":shadow_view,"depth_clear_value":1,"depth_load_op":"clear","depth_store_op":"store"})
            p.set_pipeline(shadow_pipeline);p.set_bind_group(0,shadow_group);p.set_vertex_buffer(0,caster);p.draw(3);p.end()
            p=encoder.begin_render_pass(color_attachments=[{"view":hmv,"resolve_target":hv,"load_op":"clear","store_op":"store","clear_value":(.05,.06,.07,1)}],
                depth_stencil_attachment={"view":dv,"depth_clear_value":0,"depth_load_op":"clear","depth_store_op":"store"})
            p.set_pipeline(surface_pipeline);p.set_bind_group(0,frame_group)
            for item in opaque:
                p.set_bind_group(1,object_group,[item*256]);p.set_vertex_buffer(0,near if item==0 else far);p.draw(3)
            if line:
                p.set_pipeline(line_pipeline);p.set_bind_group(1,object_group,[0]);p.set_vertex_buffer(0,ribbon);p.draw(6,1)
            p.end()
            p=encoder.begin_render_pass(color_attachments=[
                {"view":amv,"resolve_target":av,"load_op":"clear","store_op":"store","clear_value":(0,0,0,0)},
                {"view":rmv,"resolve_target":rv,"load_op":"clear","store_op":"store","clear_value":(1,1,1,1)}],
                depth_stencil_attachment={"view":dv,"depth_read_only":True})
            p.set_pipeline(ghost_pipeline);p.set_bind_group(0,frame_group)
            for item in ghost:
                p.set_bind_group(1,object_group,[item*256]);p.set_vertex_buffer(0,near if item==2 else far);p.draw(3)
            p.end()
            p=encoder.begin_compute_pass()
            for name,group in zip(bloom_pipelines,bloom_groups):
                p.set_pipeline(bloom_pipelines[name]);p.set_bind_group(0,group);p.dispatch_workgroups((half_w+7)//8,(half_h+7)//8)
            p.end()
            p=encoder.begin_render_pass(color_attachments=[{"view":ov,"load_op":"clear","store_op":"store","clear_value":(0,0,0,0)}])
            p.set_pipeline(compose);p.set_bind_group(0,compose_group);p.draw(3);p.end()
            device.queue.submit([encoder.finish()])
            pitch=((width*4+255)//256)*256
            padded=device.queue.read_texture({"texture":output},{"bytes_per_row":pitch,"rows_per_image":height},(width,height,1))
            return b"".join(padded[y*pitch:y*pitch+width*4] for y in range(height))
        return render

    render=harness(97,65)
    delta=lambda a,b:sum(abs(x-y)>1 for x,y in zip(a,b))
    max_delta=lambda a,b:max(abs(x-y) for x,y in zip(a,b))
    empty=render();near_first=render(opaque=[0,1]);far_first=render(opaque=[1,0])
    center=(32*97+48)*4
    check(delta(empty,near_first)>300,"HDR/MSAA changes real readback pixels")
    check(max_delta(near_first,far_first)<=1,"reverse-Z is independent of opaque draw order")
    check(near_first[center]>near_first[center+2]+30,"near red surface occludes far blue surface")
    check(all(x==255 for x in near_first[3::4]),"composite alpha is opaque/premultiplied")
    shadowed=render(opaque=[0],shadowed=True);unshadowed=render(opaque=[0])
    check(sum(shadowed[center:center+3])<sum(unshadowed[center:center+3])-5,"PCF shadow map attenuates key light")
    ghost_ab=render(ghost=[2,3]);ghost_ba=render(ghost=[3,2])
    check(delta(empty,ghost_ab)>300,"weighted OIT creates transparent surfaces")
    check(max_delta(ghost_ab,ghost_ba)<=1,"weighted OIT is independent of object order")
    clipped=render(opaque=[0],clip=True)
    left=(32*97+int(97*.35))*4;right=(32*97+int(97*.65))*4
    check(clipped[left]==empty[left] and abs(clipped[right]-empty[right])>20,"clip plane rejects only clipped fragments")
    lines=render(line=True)
    check(delta(empty,lines)>10,"instanced near-clipped ribbon rasterizes")
    check(lines[left]==empty[left],"near-clipped hidden ribbon does not rasterize")
    bloomed=render(opaque=[0],bloom=.6)
    check(delta(unshadowed,bloomed)>30,"separable compute bloom changes final pixels")
    partition_base=render(opaque=[0],clip=True,quad=True)
    partition_bloom=render(opaque=[0],clip=True,quad=True,bloom=.6)
    left_delta=max(abs(partition_base[(y*97+x)*4+c]-partition_bloom[(y*97+x)*4+c])
        for y in range(65) for x in range(47) for c in range(3))
    check(left_delta<=1,"bloom cannot bleed from a bright adjacent CAD viewport")
    gap=(10*97+47)*4
    check(all(abs(partition_bloom[gap+c]-expected)<=1 for c,expected in enumerate((145,148,148))),
        "viewport separator keeps its neutral display color")
    resized=harness(131,73)(opaque=[0],ghost=[3],bloom=.1)
    check(len(resized)==131*73*4 and all(x==255 for x in resized[3::4]),"odd-width resize and aligned readback preserve every row")
    report.update(status="passed",sample_count=4,color_formats=["rgba16float","r8unorm","depth32float","rgba8unorm"],dimensions=[[97,65],[131,73]])
    for resource in owned:
        resource.destroy()
    device.destroy()
    return report


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--output",default=str(ROOT/"evidence/webgpu-native-pipeline.json"))
    args=parser.parse_args()
    report={"status":"failed"}
    try:
        report=qualify()
    except Exception as error:
        report["error"]=str(error)
        raise
    finally:
        output=Path(args.output);output.parent.mkdir(parents=True,exist_ok=True)
        output.write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))


if __name__=="__main__":
    main()
