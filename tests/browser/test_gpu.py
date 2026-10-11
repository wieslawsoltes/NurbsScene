"""Strict tests for the WebGPU and WebGL2 validation-harness implementations.
Requires serving the repository over a browser-permitted localhost or HTTPS URL.
GPU absence is a failure, never a passing fallback. Not executed in delivery.
"""
import argparse,asyncio,json,os
from pathlib import Path
from playwright.async_api import async_playwright
ROOT=Path(__file__).resolve().parents[2]
async def main(url):
 reports=[]
 async with async_playwright() as pw:
  options={'headless':True}
  if os.getenv('CHROMIUM_PATH'):options['executable_path']=os.environ['CHROMIUM_PATH']
  browser=await pw.chromium.launch(**options)
  try:
   for backend in ['webgpu','webgl2']:
    page=await browser.new_page(viewport={'width':1600,'height':1100},device_scale_factor=1)
    errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
    await page.goto(url+('?backend='+backend),wait_until='domcontentloaded',timeout=60000)
    await page.wait_for_function('window.nurbsScene?.ready',timeout=60000)
    diagnostics=await page.evaluate('window.nurbsScene.diagnostics()')
    if diagnostics['renderer'].get('backend','').lower()!=backend or diagnostics['renderer']['errors']:
     raise AssertionError('Requested real GPU backend did not initialize: '+json.dumps(diagnostics))
    result=await page.evaluate('''async()=>{
      const viewport=document.querySelector('nurbs-viewport'),scene=viewport.scene;
      scene.showGrid=false;scene.showIsocurves=false;
      const items=[...scene.items];scene.setItems([]);viewport.renderer.setScene(scene);
      const empty=await viewport.capture();scene.setItems(items);viewport.renderer.setScene(scene);
      const filled=await viewport.capture();let surfacePixels=0;
      for(let i=0;i<filled.length;i+=4)if(Math.abs(filled[i]-empty[i])+Math.abs(filled[i+1]-empty[i+1])+Math.abs(filled[i+2]-empty[i+2])>12)surfacePixels++;
      const view=viewport.views.find(v=>v.name==='Perspective');view.camera.orbit(80,25);
      const rotated=await viewport.capture();let changed=0;
      for(let i=0;i<filled.length;i+=4)if(filled[i]!==rotated[i]||filled[i+1]!==rotated[i+1]||filled[i+2]!==rotated[i+2])changed++;
      return{surfacePixels,changedPixels:changed,views:viewport.views.length,diagnostics:viewport.diagnostics()};
    }''')
    if result['surfacePixels']<100 or result['changedPixels']<100 or result['views']!=4 or errors or result['diagnostics']['errors']:
     raise AssertionError('Framebuffer validation failed: '+json.dumps(result))
    await page.screenshot(path=str(ROOT/f'evidence/{backend}-frame.png'))
    result.update({'requested':backend,'pageErrors':errors,'status':'passed'});reports.append(result)
    await page.close()
  finally:
   await browser.close()
   (ROOT/'evidence/gpu-browser.json').write_text(json.dumps(reports,indent=2))
 print(json.dumps(reports,indent=2))
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--url',default='http://127.0.0.1:8765/validation-harness/');args=p.parse_args();asyncio.run(main(args.url))
