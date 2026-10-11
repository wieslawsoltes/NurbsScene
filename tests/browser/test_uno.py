"""Smoke test of the compiled Uno application, not the JS harness.
Build with -p:NurbsSceneQualification=true and serve the actual publish output.
No GPU mocks or skip-on-unavailable behavior. Browser logs are retained on failure.
"""
import argparse,asyncio,json,os
from pathlib import Path
from playwright.async_api import async_playwright
from test_workspace_ui import verify_workspace_ui
ROOT=Path(__file__).resolve().parents[2]
async def main(url):
 (ROOT/'evidence').mkdir(exist_ok=True)
 async with async_playwright() as pw:
  options={'headless':True}
  if os.getenv('CHROMIUM_PATH'):options['executable_path']=os.environ['CHROMIUM_PATH']
  browser=await pw.chromium.launch(**options)
  page=await browser.new_page(viewport={'width':1600,'height':1100},device_scale_factor=1)
  errors=[];logs=[]
  page.on('pageerror',lambda error:errors.append(str(error)))
  page.on('console',lambda msg:logs.append(msg.type+': '+msg.text))
  try:
   await page.goto(url+('&' if '?' in url else '?')+'qualification=1',wait_until='domcontentloaded',timeout=60000)
   await page.wait_for_function('globalThis.nurbsSceneQualification && globalThis.nurbsSceneQualification.status !== "running"',timeout=180000)
   report=await page.evaluate('globalThis.nurbsSceneQualification');report['pageErrors']=errors
   (ROOT/'evidence/uno-browser.json').write_text(json.dumps(report,indent=2))
   if report.get('application')!='Uno Platform' or report.get('status')!='passed' or errors:
    raise AssertionError(json.dumps(report,indent=2))
   print(json.dumps(report,indent=2))
   await verify_workspace_ui(page)
   if errors: raise AssertionError(errors)
  finally:
   await page.screenshot(path=str(ROOT/'evidence/uno-browser.png'))
   (ROOT/'evidence/uno-console.json').write_text(json.dumps({'errors':errors,'console':logs},indent=2))
   await browser.close()
if __name__=='__main__':
 parser=argparse.ArgumentParser();parser.add_argument('--url',default='http://127.0.0.1:8766/');args=parser.parse_args();asyncio.run(main(args.url))
