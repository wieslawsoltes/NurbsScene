"""Strict qualification of the reusable ESM WebGPU renderer.

Exercises the real NURBS validation workbench and real package GPU resource/pass
orchestration. Local display-mesh fixtures isolate depth, transparency, clipping,
shadow caching, rebasing and asynchronous capture; no GPU API is mocked.

Build packages/geometry/dist/nurbsscene.wasm first with tools/build-kernel.sh.
The default route serves repository bytes into a secure test origin. --url can
instead select a running validation workbench. Software correctness is labeled;
no adapter absence, shader failure or framebuffer failure is skipped.
"""
import argparse
import asyncio
import json
import mimetypes
import os
from pathlib import Path
from urllib.parse import unquote, urlsplit

from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parents[2]
SECURE = "https://nurbsscene-package.invalid"
INSECURE = "http://nurbsscene-insecure.invalid"
FIXTURE_HTML = """<!doctype html><meta charset="utf-8">
<script type="importmap">{"imports":{"@nurbsscene/":"/packages/"}}</script>
<canvas id="fixture" width="129" height="97"></canvas>"""
CONTEXT_OBSERVER = """(() => {
  window.nurbsContextRequests = [];
  const native = HTMLCanvasElement.prototype.getContext;
  HTMLCanvasElement.prototype.getContext = function(...args) {
    window.nurbsContextRequests.push(args[0]);
    return Reflect.apply(native, this, args);
  };
})();"""

HARNESS_TEST = """async () => {
  const viewport = document.querySelector('nurbs-viewport'), scene = viewport.scene;
  if (!viewport.renderer || viewport.renderer.backend !== 'WebGPU')
    throw new Error('The NURBS workbench did not initialize WebGPU: ' + JSON.stringify(viewport.diagnostics()));
  scene.showGrid = false; scene.showIsocurves = false;
  const items = [...scene.items];
  scene.setItems([]); viewport.renderer.setScene(scene);
  const empty = await viewport.capture();
  scene.setItems(items); viewport.renderer.setScene(scene);
  const filled = await viewport.capture();
  let surfacePixels = 0;
  for (let i = 0; i < filled.length; i += 4)
    if (Math.abs(filled[i] - empty[i]) + Math.abs(filled[i+1] - empty[i+1]) +
        Math.abs(filled[i+2] - empty[i+2]) > 12) surfacePixels++;
  viewport.views.find(view => view.name === 'Perspective').camera.orbit(80, 25);
  const rotated = await viewport.capture();
  let changedPixels = 0;
  for (let i = 0; i < filled.length; i += 4)
    if (filled[i] !== rotated[i] || filled[i+1] !== rotated[i+1] || filled[i+2] !== rotated[i+2]) changedPixels++;
  await viewport.renderer.device.queue.onSubmittedWorkDone();
  return { surfacePixels, changedPixels, views: viewport.views.length, diagnostics: viewport.diagnostics(),
    contextRequests: window.nurbsContextRequests };
}"""

PACKAGE_TEST = r"""async ({software}) => {
  const { createRenderer, WebGl2Renderer, RenderScene, Camera, viewportLayout } =
    await import('/packages/graphics/index.js');
  const checks = [];
  const check = (ok, name) => { if (!ok) throw new Error(name); checks.push(name); };
  const difference = (a, b, threshold = 2) => {
    check(a.length === b.length, 'compared frame dimensions agree');
    let pixels = 0, maximum = 0;
    for (let i = 0; i < a.length; i += 4) {
      const delta = Math.max(Math.abs(a[i]-b[i]), Math.abs(a[i+1]-b[i+1]), Math.abs(a[i+2]-b[i+2]));
      maximum = Math.max(maximum, delta);
      if (delta > threshold) pixels++;
    }
    return {pixels, maximum};
  };
  const canvas = document.querySelector('#fixture');
  const result = await createRenderer(canvas, {
    backend: 'auto', forceFallbackAdapter: software, shadowResolution: 256,
    shadows: false, bloomStrength: 0,
  });
  const renderer = result.renderer;
  check(result.canvas === canvas && document.querySelector('#fixture') === canvas &&
    result.failures.length === 0 && renderer.backend === 'WebGPU', 'auto returns WebGPU and preserves canvas identity');
  check(!software || renderer.info.software, 'software request is identified honestly');
  const scene = new RenderScene();
  scene.showGrid = false; scene.showIsocurves = false;
  const camera = new Camera('Top');
  camera.target = [0,0,0]; camera.distance = 10; camera.scale = 2.5;
  const view = () => [{name:'Top', x:0, y:0, width:canvas.width, height:canvas.height, camera}];
  const fixture = (id, z, extent, color, origin = [0,0,0]) => {
    const points = [[-extent,-extent,z],[extent,-extent,z],[extent,extent,z],[-extent,extent,z]];
    const bounds = {
      min: [origin[0]-extent, origin[1]-extent, origin[2]+z],
      max: [origin[0]+extent, origin[1]+extent, origin[2]+z],
    };
    const geometry = {id, bounds:() => bounds};
    const mesh = {
      source:geometry, bounds,
      vertices:new Float32Array(points.flatMap(point => [...point,0,0,1])),
      indices:new Uint32Array([0,1,2,0,2,3]),
      lines:new Float32Array([0,1,1,2,2,3,3,0].flatMap(index => points[index])),
    };
    return {item:{id,geometry,color},mesh};
  };
  const install = fixtures => {
    scene.items = fixtures.map(value => value.item);
    scene.meshes = new Map(fixtures.map(value => [value.item.geometry.id,value.mesh]));
    scene.revision++;
    renderer.setScene(scene);
  };
  const capture = async (views = view()) => {
    const pixels = await renderer.render(views,{capture:true});
    check(pixels.length === canvas.width*canvas.height*4 &&
      pixels.every((value,index) => index%4 !== 3 || value === 255), 'capture returns complete opaque RGBA pixels');
    return pixels;
  };
  const front = fixture('front', .3, .8, [.85,.12,.08,1]);
  const back = fixture('back', 0, .8, [.06,.15,.9,1]);
  renderer.device.pushErrorScope('validation');
  try {
    install([]);
    const empty = await capture();
    install([front,back]);
    const nearFirst = await capture();
    install([back,front]);
    const farFirst = await capture();
    const depthOrder = difference(nearFirst, farFirst, 0);
    check(depthOrder.pixels === 0, 'reverse-Z surfaces are independent of opaque submission order');
    const center = (Math.floor(canvas.height/2)*canvas.width+Math.floor(canvas.width/2))*4;
    check(nearFirst[center] > nearFirst[center+2]+25, 'nearest red surface wins actual depth testing');
    check(difference(empty, nearFirst).pixels > 500, 'real surface geometry changes the framebuffer');
    check(renderer.metrics.sampleCount === 4 && renderer.metrics.oitSize.join(',') === '1,1',
      'opaque rendering uses 4x MSAA and compact OIT targets');

    const ghostBehind = fixture('hidden-ghost', -.2, .45, [.1,.8,.2,.45]);
    install([front]);
    const compact = await capture();
    install([front,ghostBehind]);
    const fullOit = await capture();
    check(renderer.metrics.oitSize.join(',') === canvas.width+','+canvas.height,
      'transparent geometry allocates full-size OIT targets');
    check(difference(compact,fullOit,0).pixels === 0,
      'opaque depth rejects hidden transparency and full OIT equals the 1x1 fallback');

    const redGhost = fixture('red-ghost', .25, .8, [.9,.1,.08,.45]);
    const blueGhost = fixture('blue-ghost', 0, .8, [.08,.15,.9,.35]);
    install([redGhost,blueGhost]);
    const ghostAB = await capture();
    install([blueGhost,redGhost]);
    const ghostBA = await capture();
    check(difference(ghostAB,ghostBA).maximum <= 2, 'weighted transparency survives reversed overlapping object order');
    install([redGhost]);
    check(difference(ghostAB,await capture()).pixels > 500, 'both transparent surfaces contribute to final pixels');

    install([front]);
    scene.clipPlane = [1,0,0,0];
    const clipped = await capture();
    check(difference(compact,clipped).pixels > 250, 'clip plane removes a visible part of the real surface');
    const left = (Math.floor(canvas.height/2)*canvas.width+Math.floor(canvas.width*.35))*4;
    check(clipped.slice(left,left+4).every((value,index) => value === empty[left+index]),
      'discarded half exposes the true background');
    scene.clipPlane = null;

    const beforeUploads = renderer.geometryUploads;
    const vertexBuffer = renderer.buffers.get(front.item.geometry.id).vertex;
    camera.pan(4,2,canvas.height); await capture();
    scene.selected.add(front.item.id); await capture();
    scene.selected.clear(); camera.target = [0,0,0];
    renderer.setScene(scene); await capture();
    check(renderer.geometryUploads === beforeUploads &&
      renderer.buffers.get(front.item.geometry.id).vertex === vertexBuffer,
      'camera and selection changes reuse the same immutable GPU geometry buffers');

    const originalWidth = canvas.width, originalHeight = canvas.height;
    const pending = renderer.render(view(),{capture:true});
    canvas.width = 97; canvas.height = 65;
    const resized = await capture();
    const original = await pending;
    check(original.length === originalWidth*originalHeight*4 && resized.length === 97*65*4,
      'in-flight readback retains submission dimensions during canvas resize');
    canvas.width = originalWidth; canvas.height = originalHeight;
    const restored = await capture();
    check(difference(compact,restored,0).pixels === 0, 'odd-size target recreation preserves the rendered image');

    const origin = [1e9,-2e9,3e9];
    scene.origin = origin; camera.target = [...origin];
    install([fixture('rebased-front', .3, .8, [.85,.12,.08,1], origin)]);
    const rebased = await capture();
    check(difference(compact,rebased).maximum <= 1,
      'billion-unit world rebasing preserves actual display pixels');
    scene.origin = [0,0,0]; camera.target = [0,0,0];

    const ground = fixture('ground', 0, .95, [.75,.75,.75,1]);
    const caster = fixture('caster', .35, .25, [.65,.65,.65,1]);
    install([ground,caster]);
    renderer.setOptions({lightDirection:[-.6,-.3,1],shadows:false});
    const unshadowed = await capture();
    renderer.setOptions({shadows:true});
    const shadowed = await capture();
    let darkened = 0;
    for (let i = 0; i < shadowed.length; i += 4)
      if (unshadowed[i]+unshadowed[i+1]+unshadowed[i+2] -
          shadowed[i]-shadowed[i+1]-shadowed[i+2] > 12) darkened++;
    check(darkened > 20, 'directional PCF shadow darkens visible receiving geometry');
    const shadowPasses = renderer.shadowPasses;
    camera.pan(2,0,canvas.height); await capture();
    check(renderer.shadowPasses === shadowPasses, 'camera motion reuses the camera-independent shadow map');
    camera.target = [0,0,0];
    scene.clipPlane = [1,0,0,0]; await capture();
    check(renderer.shadowPasses === shadowPasses+1, 'clipping invalidates the shadow map');
    scene.clipPlane = null;
    renderer.setOptions({shadows:false,bloomStrength:0});
    const noBloom = await capture();
    renderer.setOptions({bloomStrength:.5,bloomThreshold:.3,bloomKnee:.2});
    check(difference(noBloom,await capture()).pixels > 100, 'compute bloom changes actual HDR composition');

    // Raise only the geometry above the extraction threshold. The neutral
    // backdrop stays below it, so an exterior halo proves OIT enters bloom.
    const brightGhost = fixture('bright-ghost', .3, .5, [1,1,1,.8]);
    renderer.setOptions({lightIntensity:8,bloomStrength:0,bloomThreshold:1.2,bloomKnee:.05});
    install([brightGhost]);
    const ghostNoBloom = await capture();
    renderer.setOptions({bloomStrength:.6});
    const ghostBloom = await capture();
    const haloY=Math.floor(canvas.height/2-.5*canvas.height/camera.scale)-3;
    const halo=(haloY*canvas.width+Math.floor(canvas.width/2))*4;
    check([0,1,2].every(channel=>ghostNoBloom[halo+channel]===empty[halo+channel]) &&
      [0,1,2].some(channel=>ghostBloom[halo+channel]>ghostNoBloom[halo+channel]+2),
      'transparent-only HDR highlights bloom outside their surface after full OIT target creation');
    install([]); const afterTransparent = await capture();
    check(difference(empty,afterTransparent,0).pixels===0 && renderer.metrics.oitSize.join(',')==='1,1',
      'returning to compact OIT removes transparent bloom without stale texture bindings');
    renderer.setOptions({lightIntensity:3.2,bloomStrength:.5,bloomThreshold:.3,bloomKnee:.2});
    install([ground,caster]);

    scene.mode = 'Ghosted'; await capture();
    check(renderer.metrics.oitSize.join(',') === canvas.width+','+canvas.height,
      'Ghosted mode enters weighted OIT');
    scene.mode = 'Wireframe';
    const wireframe = await capture();
    check(renderer.metrics.triangles === 0 && renderer.metrics.lines > 0 &&
      renderer.metrics.oitSize.join(',') === '1,1', 'Wireframe draws ribbons without transparent surface work');
    scene.mode = 'Shaded';

    const lineGeometry = {id:'near-crossing-line'};
    install([{item:{id:'near-crossing-line',geometry:lineGeometry,color:[.9,.1,.1,1]},mesh:{
      source:lineGeometry, bounds:{min:[-.8,0,0],max:[.8,0,11]},
      vertices:new Float32Array(),indices:new Uint32Array(),
      lines:new Float32Array([-.8,0,11,.8,0,0]),
    }}]);
    renderer.setOptions({bloomStrength:0});
    const ribbon = await capture();
    const ribbonPixels = difference(empty,ribbon).pixels;
    check(ribbonPixels > 20 && ribbonPixels < canvas.width*canvas.height*.2,
      'near-plane-crossing curve produces a bounded antialiased ribbon');

    install([front]);
    renderer.setOptions({bloomStrength:.5,bloomThreshold:.3});
    const cameras = Object.fromEntries(['Top','Perspective','Front','Right'].map(name => {
      const camera = new Camera(name); camera.target=[0,0,0]; camera.distance=5; camera.scale=2.5;
      return [name,camera];
    }));
    const quadViews = viewportLayout(canvas.width,canvas.height).map(view => ({...view,camera:cameras[view.name]}));
    const quad = await capture(quadViews);
    const splitX = quadViews[0].width, splitY = quadViews[0].height;
    for (let y=0;y<canvas.height;y++) for (let x=0;x<canvas.width;x++) {
      if ((x>=splitX && x<quadViews[1].x) || (y>=splitY && y<quadViews[2].y)) {
        const offset=(y*canvas.width+x)*4;
        if (![145,148,148,255].every((value,index) => quad[offset+index]===value))
          throw new Error('Quad-view separator was contaminated by HDR, transparency or bloom.');
      }
    }
    checks.push('quad-view separators remain exact through bloom and composition');

    canvas.width=1;canvas.height=1;
    const tinyViews=viewportLayout(1,1).map(value=>({...value,camera}));
    check((await capture(tinyViews)).length===4, 'one-pixel canvas renders and captures successfully');

    let rejected = 0;
    for (const initialize of [
      () => createRenderer(canvas,{backend:'webgl2'}),
      () => WebGl2Renderer.create(canvas),
    ]) {
      try { await initialize(); } catch(error) {
        if (/removed.*WebGpuRenderer.create/.test(error.message)) rejected++;
      }
    }
    check(rejected===2, 'legacy backend requests reject with actionable migration guidance');
    check(window.nurbsContextRequests.every(value=>value==='webgpu'), 'no GL or Canvas2D rendering context is requested');

    await renderer.device.queue.onSubmittedWorkDone();
    const validation = await renderer.device.popErrorScope();
    check(!validation, 'no WebGPU resource, pass, binding or submission validation error');
    check(renderer.errors.length===0, 'no uncaptured GPU error or unexpected device loss');
    const report={checks,adapter:renderer.info,metrics:renderer.metrics,
      transparencyOrder:difference(ghostAB,ghostBA),shadowPixels:darkened,ribbonPixels,
      dimensions:[[originalWidth,originalHeight],[97,65],[1,1]],contextRequests:window.nurbsContextRequests};
    renderer.dispose(); renderer.dispose();
    let disposedRejected=false;
    try { renderer.render(view()); } catch(error) { disposedRejected=/disposed/.test(error.message); }
    check(disposedRejected, 'disposed renderer rejects further submission');
    return report;
  } finally { renderer.dispose(); }
}"""

UNAVAILABLE_TEST = r"""async () => {
  if (navigator.gpu) throw new Error('Negative capability fixture must be an actual insecure context.');
  const { createRenderer } = await import('/packages/graphics/index.js');
  const canvas=document.querySelector('#fixture'),errors=[];
  for (const backend of ['auto','webgpu']) {
    try { await createRenderer(canvas,{backend}); }
    catch(error) { errors.push(error.message); }
  }
  if (errors.length!==2 || !errors.every(message=>/WebGPU.*secure/i.test(message)))
    throw new Error('Unavailable WebGPU did not fail explicitly: '+JSON.stringify(errors));
  if (document.querySelector('#fixture')!==canvas || window.nurbsContextRequests.length)
    throw new Error('Capability failure replaced the canvas or attempted a legacy rendering context.');
  return {checks:['actual unavailable WebGPU fails explicitly without canvas replacement or fallback'],
    errors,contextRequests:window.nurbsContextRequests};
}"""


async def serve_fixture(route):
    path = unquote(urlsplit(route.request.url).path)
    if path == "/__gpu.html":
        await route.fulfill(status=200, content_type="text/html", body=FIXTURE_HTML)
        return
    source = (ROOT / path.lstrip("/")).resolve()
    if source.is_dir():
        source = source / "index.html"
    if not source.is_relative_to(ROOT) or not source.is_file():
        await route.fulfill(status=404, body="Fixture resource not found")
        return
    content_type = {
        ".js": "application/javascript", ".mjs": "application/javascript", ".wasm": "application/wasm"
    }.get(source.suffix, mimetypes.guess_type(source.name)[0] or "application/octet-stream")
    await route.fulfill(status=200, content_type=content_type, body=source.read_bytes())


async def main(args):
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    launch_args = json.loads(os.getenv("NURBSSCENE_CHROMIUM_ARGS", "[]"))
    if args.software:
        launch_args += ["--enable-unsafe-webgpu", "--use-angle=swiftshader", "--enable-unsafe-swiftshader",
                        "--use-vulkan=swiftshader", "--enable-features=Vulkan"]
    launch_args = list(dict.fromkeys(launch_args))
    report = {"status": "failed", "browserLaunchArgs": launch_args, "softwareRequested": args.software,
              "qualification": "software-adapter correctness; no physical-GPU performance claim" if args.software
              else "selected-adapter correctness"}
    options = {"headless": True, "args": launch_args}
    if os.getenv("CHROMIUM_PATH"):
        options["executable_path"] = os.environ["CHROMIUM_PATH"]
    try:
        if not (ROOT / "packages/geometry/dist/nurbsscene.wasm").is_file():
            raise FileNotFoundError("Build the real NURBS kernel first: bash tools/build-kernel.sh")
        async with async_playwright() as pw:
            browser = await pw.chromium.launch(**options)
            try:
                report["browserVersion"] = browser.version
                context = await browser.new_context(viewport={"width": 1600, "height": 1100}, device_scale_factor=1)
                await context.add_init_script(CONTEXT_OBSERVER)
                await context.route(SECURE + "/**", serve_fixture)
                await context.route(INSECURE + "/**", serve_fixture)
                page_errors = []
                page = await context.new_page()
                page.on("pageerror", lambda error: page_errors.append(str(error)))
                harness_url = args.url or SECURE + "/validation-harness/"
                separator = "&" if "?" in harness_url else "?"
                await page.goto(harness_url + separator + "backend=webgpu", wait_until="domcontentloaded", timeout=60000)
                await page.wait_for_function("window.nurbsScene?.ready", timeout=90000)
                harness = await page.evaluate(HARNESS_TEST)
                report["nurbsWorkbench"] = harness
                if (harness["surfacePixels"] < 100 or harness["changedPixels"] < 100 or harness["views"] != 4
                        or harness["diagnostics"]["errors"] or page_errors):
                    raise AssertionError("NURBS framebuffer qualification failed: " + json.dumps(harness))
                if args.software and not harness["diagnostics"].get("software"):
                    raise AssertionError("NURBS workbench did not identify the requested software adapter.")
                if any(value != "webgpu" for value in harness["contextRequests"]):
                    raise AssertionError("A non-WebGPU rendering context was requested.")
                await page.screenshot(path=str(output.parent / "javascript-webgpu-frame.png"))
                await page.close()

                page = await context.new_page()
                page.on("pageerror", lambda error: page_errors.append(str(error)))
                await page.goto(SECURE + "/__gpu.html", wait_until="domcontentloaded")
                report["packagePipeline"] = await page.evaluate(PACKAGE_TEST, {"software": args.software})
                await page.close()

                page = await context.new_page()
                page.on("pageerror", lambda error: page_errors.append(str(error)))
                await page.goto(INSECURE + "/__gpu.html", wait_until="domcontentloaded")
                report["unavailableWebGpu"] = await page.evaluate(UNAVAILABLE_TEST)
                report["pageErrors"] = page_errors
                if page_errors:
                    raise AssertionError("Uncaught browser errors: " + json.dumps(page_errors))
                report["status"] = "passed"
                await context.close()
            finally:
                await browser.close()
    except Exception as error:
        report["error"] = str(error)
        raise
    finally:
        output.write_text(json.dumps(report, indent=2))
        print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", help="Optional URL of an already-served NURBS validation workbench")
    parser.add_argument("--software", action="store_true", help="Require and explicitly label the software WebGPU adapter")
    parser.add_argument("--output", default=str(ROOT / "evidence/javascript-webgpu.json"))
    asyncio.run(main(parser.parse_args()))
