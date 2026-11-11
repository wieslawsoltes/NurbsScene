"""Execute the production WGSL pipeline and inspect real GPU readback.

This qualifies shaders and pass/format contracts independently of Uno integration.
Use --software explicitly on CI; its report never calls software rasterization a
physical-GPU performance result. Adapter absence or any validation error fails.
"""
import argparse
import asyncio
import json
import os
from pathlib import Path

from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[2]
SHADERS = ROOT / "src/NurbsScene.Rendering/Shaders"

GPU_TEST = r"""async ({sources, software}) => {
  if (!navigator.gpu) throw new Error('WebGPU is unavailable in this browser');
  const adapter = await navigator.gpu.requestAdapter({forceFallbackAdapter: software, powerPreference: 'high-performance'});
  if (!adapter) throw new Error('Requested WebGPU adapter is unavailable');
  const device = await adapter.requestDevice();
  const errors = [];
  device.addEventListener('uncapturederror', event => errors.push(event.error.message));
  const info = adapter.info;
  const report = {
    adapter: {vendor: info?.vendor, architecture: info?.architecture, device: info?.device,
      description: info?.description, isFallbackAdapter: info?.isFallbackAdapter},
    qualification: software ? 'software-adapter correctness; no physical-GPU performance claim' : 'selected-adapter correctness',
    shaderMessages: {}, checks: []
  };
  const check = (ok, name) => { if (!ok) throw new Error(name); report.checks.push(name); };
  const modules = {};
  for (const [name, code] of Object.entries(sources)) {
    modules[name] = device.createShaderModule({label: name, code});
    const diagnostics = await modules[name].getCompilationInfo();
    report.shaderMessages[name] = [...diagnostics.messages].map(m => ({type:m.type, line:m.lineNum, column:m.linePos, message:m.message}));
    check(!diagnostics.messages.some(m => m.type === 'error'), name + ' compiles');
  }
  device.pushErrorScope('validation');
  const owned = [];
  const buffer = (data, usage) => {
    const result = device.createBuffer({size: Math.max(4, data.byteLength), usage: usage | GPUBufferUsage.COPY_DST});
    device.queue.writeBuffer(result, 0, data); owned.push(result); return result;
  };
  const texture = (width, height, format, sampleCount = 1, extra = 0) => {
    const result = device.createTexture({size:[width,height], format, sampleCount,
      usage: GPUTextureUsage.RENDER_ATTACHMENT | extra});
    owned.push(result); return result;
  };
  const frameBytes = new Float32Array(64);
  for (const offset of [0,16]) for (let i=0;i<4;i++) frameBytes[offset+i*5]=1;
  frameBytes.set([0,0,3,1],32);
  frameBytes.set([0,0,0,0],36);
  frameBytes.set([-.3,-.5,.85,0],44);
  frameBytes.set([0,.55,3.2,0],48);
  const frame = buffer(frameBytes, GPUBufferUsage.UNIFORM);
  const objectBytes = new Float32Array(64 * 4);
  const object = buffer(objectBytes, GPUBufferUsage.UNIFORM);
  const shadow = texture(256,256,'depth32float',1,GPUTextureUsage.TEXTURE_BINDING);
  const shadowView = shadow.createView();
  const shadowSampler = device.createSampler({compare:'less-equal', minFilter:'linear', magFilter:'linear'});
  const frameLayout = device.createBindGroupLayout({entries:[
    {binding:0,visibility:GPUShaderStage.VERTEX|GPUShaderStage.FRAGMENT,buffer:{type:'uniform',minBindingSize:256}},
    {binding:1,visibility:GPUShaderStage.FRAGMENT,texture:{sampleType:'depth'}},
    {binding:2,visibility:GPUShaderStage.FRAGMENT,sampler:{type:'comparison'}}]});
  const shadowLayout = device.createBindGroupLayout({entries:[
    {binding:0,visibility:GPUShaderStage.VERTEX|GPUShaderStage.FRAGMENT,buffer:{type:'uniform',minBindingSize:256}}]});
  const objectLayout = device.createBindGroupLayout({entries:[
    {binding:0,visibility:GPUShaderStage.VERTEX|GPUShaderStage.FRAGMENT,buffer:{type:'uniform',hasDynamicOffset:true,minBindingSize:32}}]});
  const frameGroup = device.createBindGroup({layout:frameLayout,entries:[
    {binding:0,resource:{buffer:frame,size:256}},{binding:1,resource:shadowView},{binding:2,resource:shadowSampler}]});
  const shadowGroup = device.createBindGroup({layout:shadowLayout,entries:[{binding:0,resource:{buffer:frame,size:256}}]});
  const objectGroup = device.createBindGroup({layout:objectLayout,entries:[{binding:0,resource:{buffer:object,size:32}}]});
  const sceneLayout = device.createPipelineLayout({bindGroupLayouts:[frameLayout,objectLayout]});
  const vertices = {arrayStride:24,attributes:[{shaderLocation:0,format:'float32x3',offset:0},{shaderLocation:1,format:'float32x3',offset:12}]};
  const surfacePipeline = await device.createRenderPipelineAsync({layout:sceneLayout,
    vertex:{module:modules.scene,entryPoint:'surfaceVertex',buffers:[vertices]},
    fragment:{module:modules.scene,entryPoint:'surfaceFragment',targets:[{format:'rgba16float'}]},
    primitive:{topology:'triangle-list',cullMode:'none'},
    depthStencil:{format:'depth32float',depthWriteEnabled:true,depthCompare:'greater-equal'},multisample:{count:4}});
  const add = {operation:'add',srcFactor:'one',dstFactor:'one'};
  const reveal = {operation:'add',srcFactor:'zero',dstFactor:'one-minus-src-alpha'};
  const ghostPipeline = await device.createRenderPipelineAsync({layout:sceneLayout,
    vertex:{module:modules.scene,entryPoint:'surfaceVertex',buffers:[vertices]},
    fragment:{module:modules.scene,entryPoint:'ghostFragment',targets:[
      {format:'rgba16float',blend:{color:add,alpha:add}},
      {format:'r8unorm',blend:{color:reveal,alpha:reveal}}]},
    primitive:{topology:'triangle-list',cullMode:'none'},
    depthStencil:{format:'depth32float',depthWriteEnabled:false,depthCompare:'greater-equal'},multisample:{count:4}});
  const linePipeline = await device.createRenderPipelineAsync({layout:sceneLayout,
    vertex:{module:modules.scene,entryPoint:'lineVertex',buffers:[{...vertices,stepMode:'instance'}]},
    fragment:{module:modules.scene,entryPoint:'lineFragment',targets:[{format:'rgba16float',blend:{
      color:{operation:'add',srcFactor:'src-alpha',dstFactor:'one-minus-src-alpha'},
      alpha:{operation:'add',srcFactor:'one',dstFactor:'one-minus-src-alpha'}}}]},
    primitive:{topology:'triangle-list',cullMode:'none'},
    depthStencil:{format:'depth32float',depthWriteEnabled:false,depthCompare:'greater-equal'},multisample:{count:4}});
  const shadowPipeline = await device.createRenderPipelineAsync({layout:device.createPipelineLayout({bindGroupLayouts:[shadowLayout]}),
    vertex:{module:modules.scene,entryPoint:'shadowVertex',buffers:[vertices]},
    fragment:{module:modules.scene,entryPoint:'shadowFragment',targets:[]},
    primitive:{topology:'triangle-list',cullMode:'none'},
    depthStencil:{format:'depth32float',depthWriteEnabled:true,depthCompare:'less-equal'}});
  const bloomLayout = device.createBindGroupLayout({entries:[
    {binding:0,visibility:GPUShaderStage.COMPUTE,texture:{sampleType:'float'}},
    {binding:1,visibility:GPUShaderStage.COMPUTE,storageTexture:{access:'write-only',format:'rgba16float'}},
    {binding:2,visibility:GPUShaderStage.COMPUTE,buffer:{type:'uniform',minBindingSize:32}}]});
  const bloomPipelineLayout = device.createPipelineLayout({bindGroupLayouts:[bloomLayout]});
  const bloomPipelines = {};
  for (const entryPoint of ['downsample','blurHorizontal','blurVertical'])
    bloomPipelines[entryPoint] = await device.createComputePipelineAsync({layout:bloomPipelineLayout,compute:{module:modules.bloom,entryPoint}});
  const bloomParameters = buffer(new Float32Array([.3,.1,0,0,0,0,0,0]),GPUBufferUsage.UNIFORM);
  const postParameters = buffer(new Float32Array([1,0,1,0,0,0,0,0]),GPUBufferUsage.UNIFORM);
  const compose = await device.createRenderPipelineAsync({layout:'auto',
    vertex:{module:modules.composite,entryPoint:'fullscreenVertex'},
    fragment:{module:modules.composite,entryPoint:'compositeFragment',targets:[{format:'rgba8unorm'}]},
    primitive:{topology:'triangle-list'}});
  const linearSampler = device.createSampler({minFilter:'linear',magFilter:'linear'});
  const triangle = z => buffer(new Float32Array([
    -.85,-.8,z, 0,0,1, .85,-.8,z, 0,0,1, 0,.85,z, 0,0,1]),GPUBufferUsage.VERTEX);
  const near = triangle(.7), far = triangle(.25), caster = triangle(.2);
  const ribbon = buffer(new Float32Array([-.9,0,2, .9,0,.5]),GPUBufferUsage.VERTEX);
  function setObject(slot,color,roughness=.36) {
    objectBytes.set(color,slot*64); objectBytes.set([.12,roughness,3,0],slot*64+4);
  }
  setObject(0,[.85,.14,.08,1]);setObject(1,[.07,.2,.9,1]);
  setObject(2,[.85,.14,.08,.45]);setObject(3,[.07,.2,.9,.35]);
  device.queue.writeBuffer(object,0,objectBytes);
  let frames=0;
  async function harness(width,height) {
    frameBytes.set([width,height,1/width,1/height],40);
    const halfW=Math.ceil(width/2),halfH=Math.ceil(height/2);
    const sampled=GPUTextureUsage.TEXTURE_BINDING;
    const hdr=texture(width,height,'rgba16float',1,sampled);
    const hdrMs=texture(width,height,'rgba16float',4);
    const accum=texture(width,height,'rgba16float',1,sampled);
    const accumMs=texture(width,height,'rgba16float',4);
    const revealTex=texture(width,height,'r8unorm',1,sampled);
    const revealMs=texture(width,height,'r8unorm',4);
    const depth=texture(width,height,'depth32float',4);
    const output=texture(width,height,'rgba8unorm',1,GPUTextureUsage.COPY_SRC);
    const bloomA=device.createTexture({size:[halfW,halfH],format:'rgba16float',usage:sampled|GPUTextureUsage.STORAGE_BINDING});
    const bloomB=device.createTexture({size:[halfW,halfH],format:'rgba16float',usage:sampled|GPUTextureUsage.STORAGE_BINDING});
    owned.push(bloomA,bloomB);
    const hdrView=hdr.createView(),hdrMsView=hdrMs.createView(),accumView=accum.createView(),accumMsView=accumMs.createView();
    const revealView=revealTex.createView(),revealMsView=revealMs.createView(),depthView=depth.createView(),outputView=output.createView();
    const bloomAView=bloomA.createView(),bloomBView=bloomB.createView();
    const bloomGroup=(source,target)=>device.createBindGroup({layout:bloomLayout,entries:[
      {binding:0,resource:source},{binding:1,resource:target},{binding:2,resource:{buffer:bloomParameters}}]});
    const bloomGroups=[bloomGroup(hdrView,bloomAView),bloomGroup(bloomAView,bloomBView),bloomGroup(bloomBView,bloomAView)];
    const composeGroup=device.createBindGroup({layout:compose.getBindGroupLayout(0),entries:[
      {binding:0,resource:hdrView},{binding:1,resource:accumView},{binding:2,resource:revealView},
      {binding:3,resource:bloomAView},{binding:4,resource:linearSampler},{binding:5,resource:{buffer:postParameters}}]});
    const pitch=Math.ceil(width*4/256)*256;
    const readback=device.createBuffer({size:pitch*height,usage:GPUBufferUsage.COPY_DST|GPUBufferUsage.MAP_READ});owned.push(readback);
    async function render({opaque=[],ghost=[],shadowed=false,clip=false,line=false,bloom=0,quad=false}={}) {
      frameBytes.set([width,height,1/width,1/height],40);
      frameBytes[47]=shadowed?1:0;frameBytes[48]=clip?1:0;frameBytes.set([1,0,0,0],36);
      device.queue.writeBuffer(frame,0,frameBytes);
      const partitions=quad?[Math.floor((width-2)/2),Math.floor((height-2)/2),2,2]:[0,0,0,0];
      device.queue.writeBuffer(postParameters,0,new Float32Array([1,bloom,1,quad?1:0,...partitions]));
      device.queue.writeBuffer(bloomParameters,0,new Float32Array([.3,.1,quad?1:0,0,...partitions]));
      const encoder=device.createCommandEncoder();
      let pass=encoder.beginRenderPass({colorAttachments:[],depthStencilAttachment:{view:shadowView,depthClearValue:1,depthLoadOp:'clear',depthStoreOp:'store'}});
      pass.setPipeline(shadowPipeline);pass.setBindGroup(0,shadowGroup);pass.setVertexBuffer(0,caster);pass.draw(3);pass.end();
      pass=encoder.beginRenderPass({colorAttachments:[{view:hdrMsView,resolveTarget:hdrView,loadOp:'clear',storeOp:'store',clearValue:[.05,.06,.07,1]}],
        depthStencilAttachment:{view:depthView,depthClearValue:0,depthLoadOp:'clear',depthStoreOp:'store'}});
      pass.setPipeline(surfacePipeline);pass.setBindGroup(0,frameGroup);
      for(const item of opaque) {pass.setBindGroup(1,objectGroup,[item*256]);pass.setVertexBuffer(0,item===0?near:far);pass.draw(3);}
      if(line){pass.setPipeline(linePipeline);pass.setBindGroup(1,objectGroup,[0]);pass.setVertexBuffer(0,ribbon);pass.draw(6,1);}
      pass.end();
      pass=encoder.beginRenderPass({colorAttachments:[
        {view:accumMsView,resolveTarget:accumView,loadOp:'clear',storeOp:'store',clearValue:[0,0,0,0]},
        {view:revealMsView,resolveTarget:revealView,loadOp:'clear',storeOp:'store',clearValue:[1,1,1,1]}],
        depthStencilAttachment:{view:depthView,depthReadOnly:true}});
      pass.setPipeline(ghostPipeline);pass.setBindGroup(0,frameGroup);
      for(const item of ghost){pass.setBindGroup(1,objectGroup,[item*256]);pass.setVertexBuffer(0,item===2?near:far);pass.draw(3);}
      pass.end();
      const compute=encoder.beginComputePass();
      for(const [i,name] of ['downsample','blurHorizontal','blurVertical'].entries()) {
        compute.setPipeline(bloomPipelines[name]);compute.setBindGroup(0,bloomGroups[i]);compute.dispatchWorkgroups(Math.ceil(halfW/8),Math.ceil(halfH/8));
      }
      compute.end();
      pass=encoder.beginRenderPass({colorAttachments:[{view:outputView,loadOp:'clear',storeOp:'store',clearValue:[0,0,0,0]}]});
      pass.setPipeline(compose);pass.setBindGroup(0,composeGroup);pass.draw(3);pass.end();
      encoder.copyTextureToBuffer({texture:output},{buffer:readback,bytesPerRow:pitch,rowsPerImage:height},[width,height,1]);
      device.queue.submit([encoder.finish()]);await readback.mapAsync(GPUMapMode.READ);
      const padded=new Uint8Array(readback.getMappedRange()),pixels=new Uint8Array(width*height*4);
      for(let y=0;y<height;y++)pixels.set(padded.subarray(y*pitch,y*pitch+width*4),y*width*4);
      readback.unmap();frames++;return pixels;
    }
    return {render,width,height};
  }
  const h=await harness(97,65);
  const empty=await h.render();
  const nearFirst=await h.render({opaque:[0,1]});
  const farFirst=await h.render({opaque:[1,0]});
  const center=(Math.floor(h.height/2)*h.width+Math.floor(h.width/2))*4;
  const delta=(a,b)=>a.reduce((n,v,i)=>n+(Math.abs(v-b[i])>1?1:0),0);
  const maxDelta=(a,b)=>a.reduce((n,v,i)=>Math.max(n,Math.abs(v-b[i])),0);
  check(delta(empty,nearFirst)>300,'HDR/MSAA surface changes real readback pixels');
  check(maxDelta(nearFirst,farFirst)<=1,'reverse-Z resolves near surface independently of draw order');
  check(nearFirst[center]>nearFirst[center+2]+30,'near red surface occludes farther blue surface');
  check(nearFirst.every((v,i)=>i%4!==3||v===255),'composite output is opaque premultiplied RGBA');
  const shadowed=await h.render({opaque:[0],shadowed:true});
  const unshadowed=await h.render({opaque:[0]});
  check(shadowed[center]+shadowed[center+1]+shadowed[center+2]<unshadowed[center]+unshadowed[center+1]+unshadowed[center+2]-5,
    'PCF shadow texture attenuates the key light');
  const ghostAB=await h.render({ghost:[2,3]});const ghostBA=await h.render({ghost:[3,2]});
  check(delta(empty,ghostAB)>300,'weighted OIT produces visible transparent surfaces');
  check(maxDelta(ghostAB,ghostBA)<=1,'weighted OIT is independent of transparent object order');
  const clipped=await h.render({opaque:[0],clip:true});
  const left=(Math.floor(h.height/2)*h.width+Math.floor(h.width*.35))*4;
  const right=(Math.floor(h.height/2)*h.width+Math.floor(h.width*.65))*4;
  check(clipped[left]===empty[left] && Math.abs(clipped[right]-empty[right])>20,'clip plane applies to shaded fragments');
  const lines=await h.render({line:true});
  check(delta(empty,lines)>10,'instanced line ribbons rasterize after near-plane clipping');
  check(lines[left]===empty[left],'near-plane clipping suppresses the hidden part of a curve');
  const bloomed=await h.render({opaque:[0],bloom:.6});
  check(delta(unshadowed,bloomed)>30,'compute bloom modifies the HDR composite');
  const partitionBase=await h.render({opaque:[0],clip:true,quad:true});
  const partitionBloom=await h.render({opaque:[0],clip:true,quad:true,bloom:.6});
  let leftDelta=0;
  for(let y=0;y<65;y++)for(let x=0;x<47;x++)for(let c=0;c<3;c++)
    leftDelta=Math.max(leftDelta,Math.abs(partitionBase[(y*97+x)*4+c]-partitionBloom[(y*97+x)*4+c]));
  check(leftDelta<=1,'bloom cannot bleed from a bright neighboring CAD viewport');
  const gap=(10*97+47)*4;
  check([145,148,148].every((expected,c)=>Math.abs(partitionBloom[gap+c]-expected)<=1),
    'viewport separator keeps its neutral display color');
  const resized=await harness(131,73);const resizedImage=await resized.render({opaque:[0],ghost:[3],bloom:.1});
  check(resizedImage.length===131*73*4 && resizedImage.every((v,i)=>i%4!==3||v===255),
    'odd-width resize and aligned texture readback preserve complete rows');
  await device.queue.onSubmittedWorkDone();
  const scopedError=await device.popErrorScope();
  check(!scopedError,'no WebGPU validation error: '+scopedError?.message);
  check(errors.length===0,'no uncaptured WebGPU errors: '+errors.join('; '));
  report.dimensions=[[h.width,h.height],[resized.width,resized.height]];
  report.colorFormats=['rgba16float','r8unorm','depth32float','rgba8unorm'];
  report.sampleCount=4;report.frames=frames;report.status='passed';
  for(const resource of owned)resource.destroy();device.destroy();
  return report;
}"""


async def main(args):
    shaders = {name: (SHADERS / f"{name}.wgsl").read_text() for name in ("scene", "bloom", "composite")}
    launch_args = json.loads(os.getenv("NURBSSCENE_CHROMIUM_ARGS", "[]"))
    if args.software:
        launch_args += ["--enable-unsafe-webgpu", "--use-angle=swiftshader"]
    options = {"headless": True, "args": launch_args}
    if os.getenv("CHROMIUM_PATH"):
        options["executable_path"] = os.environ["CHROMIUM_PATH"]
    report = {"status": "failed", "browserLaunchArgs": launch_args, "softwareRequested": args.software}
    try:
        async with async_playwright() as pw:
            browser = await pw.chromium.launch(**options)
            try:
                page = await browser.new_page()
                # A fulfilled HTTPS origin is secure without requiring an external host or network.
                await page.route("https://nurbsscene-gpu.invalid/", lambda route: route.fulfill(
                    status=200, content_type="text/html", body="<!doctype html><title>NurbsScene WGSL qualification</title>"))
                await page.goto("https://nurbsscene-gpu.invalid/")
                report.update(await page.evaluate(GPU_TEST, {"sources": shaders, "software": args.software}))
            finally:
                await browser.close()
    except Exception as error:
        report["error"] = str(error)
        raise
    finally:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, indent=2))
        print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--software", action="store_true", help="Explicitly request a software adapter; correctness only.")
    parser.add_argument("--output", default=str(ROOT / "evidence/webgpu-pipeline.json"))
    asyncio.run(main(parser.parse_args()))
