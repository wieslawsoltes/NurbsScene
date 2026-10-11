"""Offline Chromium validation. Does not assert GPU success or qualify the Uno app.
Uses page.set_content with local code; no URL policy overrides, network or context mocks.
"""
import asyncio,json,os,unittest
from pathlib import Path
from playwright.async_api import async_playwright
ROOT=Path(__file__).resolve().parents[2]
class OfflineBrowserTests(unittest.IsolatedAsyncioTestCase):
 async def asyncSetUp(self):
  self.pw=await async_playwright().start()
  self.browser=await self.pw.chromium.launch(executable_path=os.getenv('CHROMIUM_PATH','/usr/bin/chromium'),headless=True,args=['--no-sandbox'])
  self.page=await self.browser.new_page(viewport={'width':1600,'height':1100},device_scale_factor=1)
  self.errors=[];self.page.on('pageerror',lambda e:self.errors.append(str(e)))
  await self.page.set_content((ROOT/'validation-harness/offline.html').read_text())
  await self.page.wait_for_function('window.nurbsScene?.ready',timeout=15000)
  await self.page.wait_for_timeout(100)
 async def asyncTearDown(self):
  try:
   self.assertEqual(self.errors,[])
   self.assertEqual(await self.page.evaluate('window.nurbsScene?.errors??[]'),[])
  finally:await self.browser.close();await self.pw.stop()
 async def state(self,expr):return await self.page.evaluate('()=>'+expr)
 async def command(self,text):
  await self.page.locator('#command').fill(text);await self.page.locator('#command').press('Enter');await self.page.wait_for_timeout(50)
 async def test_01_kernel_graph_runs_with_honest_gpu_failure(self):
  d=await self.state('window.nurbsScene.diagnostics()')
  self.assertEqual(d['graph']['definitions'],102);self.assertEqual(d['graph']['errors'],0);self.assertEqual(d['documents'],{'objects':5,'previews':12})
  self.assertEqual(d['errors'],[])
  if d['renderer']['errors']:self.assertIn('GPU',await self.page.locator('#gpu-status').inner_text())
  (ROOT/'evidence/browser-offline-diagnostics.json').write_text(json.dumps(d,indent=2))
 async def test_02_slider_changes_exact_surface_incrementally(self):
  before=await self.state('window.nurbsScene.result.nodes.get("roof").outputs.S.allItems[0].pointAt(.5,.5)')
  slider=self.page.locator('input[type=range][data-node-id=height]');await slider.focus();await slider.press('End');await slider.press('ArrowLeft')
  await self.page.wait_for_function('window.nurbsScene.graph.node("height").parameters.value>40&&window.nurbsScene.result.cached>=3')
  after=await self.state('window.nurbsScene.result.nodes.get("roof").outputs.S.allItems[0].pointAt(.5,.5)');self.assertNotEqual(before[2],after[2])
  self.assertEqual(await self.state('window.nurbsScene.result.errors.length'),0)
 async def test_03_slider_pointer_drag_continues_across_input_events(self):
  slider=self.page.locator('input[type=range][data-node-id=height]');box=await slider.bounding_box()
  await self.page.mouse.move(box['x']+box['width']*.3,box['y']+box['height']/2);await self.page.mouse.down();await self.page.mouse.move(box['x']+box['width']*.9,box['y']+box['height']/2,steps=12);await self.page.mouse.up();await self.page.wait_for_timeout(150)
  self.assertGreater(await self.state('window.nurbsScene.graph.node("height").parameters.value'),35)
 async def test_04_node_drag_persists_and_undo_restores(self):
  header=self.page.locator('[data-node-id=roof] .ns-node-header');box=await header.bounding_box();before=await self.state('window.nurbsScene.graph.node("roof").x')
  await self.page.mouse.move(box['x']+35,box['y']+5);await self.page.mouse.down();await self.page.mouse.move(box['x']+105,box['y']+30,steps=8);await self.page.mouse.up()
  self.assertGreater(await self.state('window.nurbsScene.graph.node("roof").x'),before+30)
  await self.page.locator('#graph-undo').click();await self.page.wait_for_timeout(100);self.assertEqual(await self.state('window.nurbsScene.graph.node("roof").x'),before)
 async def test_05_search_add_connect_typed_ports_by_drag(self):
  await self.page.locator('#component-search').fill('Circle');await self.page.locator('#component-buttons button[data-component=Circle]').click();await self.page.wait_for_timeout(100)
  circle=await self.state('window.nurbsScene.graph.nodes.find(n=>n.component==="Circle").id')
  await self.page.evaluate('(id)=>{const e=document.querySelector("nurbs-graph-editor");e.graph.updateNode(id,{x:520,y:20});e.fit();}',circle)
  source=self.page.locator('.ns-port[data-node-id=height][data-direction=out]');target=self.page.locator(f'.ns-port[data-node-id="{circle}"][data-port=Radius][data-direction=in]')
  a=await source.bounding_box();b=await target.bounding_box();await self.page.mouse.move(a['x']+a['width']/2,a['y']+a['height']/2);await self.page.mouse.down();await self.page.mouse.move(b['x']+b['width']/2,b['y']+b['height']/2,steps=8);await self.page.mouse.up();await self.page.wait_for_timeout(120)
  self.assertTrue(await self.page.evaluate('(id)=>window.nurbsScene.graph.wires.some(w=>w.from.node==="height"&&w.to.node===id&&w.to.port==="Radius")',circle))
  self.assertEqual(await self.page.evaluate('(id)=>window.nurbsScene.result.nodes.get(id).outputs.C.allItems[0].pointAt(0)[0]',circle),12)
 async def test_06_command_document_undo_redo(self):
  await self.command('Circle 0 0 0 5');self.assertEqual(await self.state('window.nurbsScene.document.objects.length'),6)
  await self.command('Undo');self.assertEqual(await self.state('window.nurbsScene.document.objects.length'),5)
  await self.command('Redo');self.assertEqual(await self.state('window.nurbsScene.document.objects.length'),6)
  await self.command('BooleanUnion');self.assertIn('not implemented',await self.page.locator('#toast').inner_text());self.assertEqual(await self.state('window.nurbsScene.document.objects.length'),6)
 async def test_07_bake_exact_preview_undo(self):
  await self.page.locator('[data-node-id=roof] .ns-node-header').click();await self.page.locator('#bake').click()
  self.assertEqual(await self.state('window.nurbsScene.document.objects.length'),6)
  self.assertEqual(await self.state('window.nurbsScene.document.objects.at(-1).geometry.degreeU'),3)
  await self.command('Undo');self.assertEqual(await self.state('window.nurbsScene.document.objects.length'),5)
 async def test_08_layer_visibility_changes_scene_not_geometry(self):
  await self.page.locator('#layers-tab').click();await self.page.locator('.layer-row').filter(has_text='Structure').locator('button').first.click()
  self.assertEqual(await self.state('document.querySelector("nurbs-viewport").scene.items.length'),13)
  self.assertEqual(await self.state('window.nurbsScene.document.objects.length'),5)
 async def test_09_exact_workspace_load_and_invalid_import_atomicity(self):
  source=await self.state('window.nurbsScene.serialize()');await self.command('Circle 0 0 0 6')
  await self.page.locator('#open-file').set_input_files({'name':'test.nurbs.json','mimeType':'application/json','buffer':source.encode()});await self.page.wait_for_timeout(200)
  self.assertEqual(await self.state('window.nurbsScene.document.objects.length'),5)
  await self.page.locator('#open-file').set_input_files({'name':'invalid.json','mimeType':'application/json','buffer':b'{"format":"3dm"}'});await self.page.wait_for_timeout(100)
  self.assertEqual(await self.state('window.nurbsScene.document.objects.length'),5);self.assertIn('Unsupported',await self.page.locator('#toast').inner_text())
 async def test_10_names_are_text_not_markup(self):
  await self.page.evaluate('window.nurbsScene.graph.updateNode("roof",{name:"<img src=x onerror=window.injected=true>"})')
  self.assertIsNone(await self.state('window.injected??null'));self.assertEqual(await self.page.locator('.ns-node img').count(),0)
 async def test_11_mobile_controls_fit_without_document_overflow(self):
  await self.page.set_viewport_size({'width':700,'height':950});await self.page.wait_for_timeout(100)
  self.assertTrue(await self.state('document.documentElement.scrollWidth<=innerWidth'))
  self.assertFalse(await self.page.locator('.inspector').is_visible())
  await self.page.screenshot(path=str(ROOT/'evidence/browser-offline-mobile.png'))
 async def test_12_graph_selection_delete_and_graph_undo(self):
  await self.page.locator('[data-node-id=panel] .ns-node-header').click();await self.page.locator('#graph-editor').press('Delete');self.assertEqual(await self.state('window.nurbsScene.graph.nodes.length'),7)
  await self.page.wait_for_function('window.nurbsScene.result.nodes.size===7')
  self.assertIsNone(await self.state('window.nurbsScene.selectedNode'))
  self.assertEqual(await self.page.locator('#node-properties').inner_text(),'No component selected')
  self.assertEqual(await self.state('window.nurbsScene.errors'),[])
  await self.page.locator('#graph-undo').click();await self.page.wait_for_timeout(100);self.assertEqual(await self.state('window.nurbsScene.graph.nodes.length'),8)
  await self.page.locator('[data-node-id=roof] .ns-node-header').click();await self.page.wait_for_timeout(100);await self.page.locator('#toast').evaluate('(e)=>e.className=""')
  await self.page.screenshot(path=str(ROOT/'evidence/browser-offline-desktop.png'))
if __name__=='__main__':unittest.main(verbosity=2)
