"""Mandatory real-input campaign for all four panel borders and corners.
The temporary definition is created/deleted through keyboard and native controls;
no observation hook mutates the app. Each resize is independently undone.
"""
import json
from pathlib import Path
from ui_observation import read_ui, wait_for_ui, settle
ROOT = Path(__file__).resolve().parents[2]

async def verify_panel_edges_ui(page):
    checks = []
    async def click(identity):
        data = await wait_for_ui(page, '!!globalThis.nurbsSceneUi.controls[' + json.dumps(identity) + ']')
        b = data['controls'][identity]
        if not b['enabled']: raise AssertionError('Disabled ' + identity)
        await page.mouse.click(b['x'] + b['width']/2, b['y'] + b['height']/2)
        return await settle(page)
    def check(value, name):
        if not value: raise AssertionError(name)
        checks.append(name)
        print('EDGE CHECK ' + name, flush=True)
    await click('GraphDockBottom'); original = await click('GraphFit')
    b = original['controls']['GraphCanvas']
    await page.mouse.click(b['x'] + 2, b['y'] + 2)
    await page.keyboard.press('Control+A'); await page.keyboard.press('Delete')
    await wait_for_ui(page, 'globalThis.nurbsSceneUi.state.nodeCount===0')
    # A single isolated node permits a readable fit at every input grip, avoiding
    # a low-zoom port hit region overlapping a corner in a densely packed graph.
    await page.mouse.dblclick(b['x'] + b['width']/2, b['y'] + 30, delay=80)
    await wait_for_ui(page, 'globalThis.nurbsSceneUi.state.searchOpen')
    await click('CanvasComponentSearch'); await page.keyboard.insert_text('//Resize every edge')
    await page.keyboard.press('Enter'); await wait_for_ui(page, 'globalThis.nurbsSceneUi.state.nodeCount===1 && !globalThis.nurbsSceneUi.state.searchOpen')
    initial = await click('GraphFit')
    identity = initial['nodes'][0]['id']
    def node(data): return next(n for n in data['nodes'] if n['id'] == identity)
    for name, xside, yside in [('left',-1,0), ('right',1,0), ('top',0,-1), ('bottom',0,1),
                              ('top-left',-1,-1), ('top-right',1,-1), ('bottom-left',-1,1), ('bottom-right',1,1)]:
        before = await read_ui(page); n = node(before)
        x = n['x'] + (2 if xside<0 else n['width']-2 if xside>0 else n['width']/2)
        y = n['y'] + (2 if yside<0 else n['height']-2 if yside>0 else n['height']/2)
        dx, dy = xside*18, yside*12
        await page.mouse.move(x,y); await page.mouse.down(); await page.mouse.move(x+dx,y+dy,steps=6)
        draft = await settle(page)
        check(draft['values']['resizeBounds'] is not None and draft['state']['graphRevision']==before['state']['graphRevision'],
              name + ' handle keeps pointer motion outside graph history')
        await page.mouse.up(); after = await settle(page); resized = node(after)
        check(abs(resized['width']-n['width']-abs(dx))<.2 and abs(resized['height']-n['height']-abs(dy))<.2,
              name + ' handle updates only its selected dimensions')
        check(abs((resized['x']+resized['width'] if xside<0 else resized['x']) - (n['x']+n['width'] if xside<0 else n['x']))<.2 and
              abs((resized['y']+resized['height'] if yside<0 else resized['y']) - (n['y']+n['height'] if yside<0 else n['y']))<.2,
              name + ' keeps the opposite edges anchored')
        check(after['state']['graphRevision']==before['state']['graphRevision']+1 and after['state']['graphErrors']==0,
              name + ' commits one valid revision')
        if name=='top-left': await page.screenshot(path=str(ROOT/'evidence/uno-panel-edge-handles.png'))
        restored = await click('GraphUndo'); r = node(restored)
        check(abs(r['width']-n['width'])<.2 and abs(r['height']-n['height'])<.2 and r['modelX']==n['modelX'] and r['modelY']==n['modelY'],
              name + ' undoes position and dimensions together')
    before = await read_ui(page); n = node(before)
    await page.mouse.move(n['x']+2,n['y']+n['height']/2); await page.mouse.down()
    await page.mouse.move(n['x']-20,n['y']+n['height']/2,steps=5); await settle(page)
    await page.keyboard.press('Escape'); await page.mouse.up(); cancelled=await settle(page)
    check(cancelled['values']['resizeBounds'] is None and cancelled['state']['graphRevision']==before['state']['graphRevision'] and cancelled['state']['graphRedo'],
          'Escape cancels left-edge motion without clearing redo')
    await click('GraphUndo')  # Remove the temporary panel.
    await click('GraphUndo')  # Restore the original definition, including wires.
    await click('GraphDockRight'); final=await click('GraphFit')
    check({n['id'] for n in final['nodes']}=={n['id'] for n in original['nodes']} and
          final['state']['wireCount']==original['state']['wireCount'] and final['state']['graphErrors']==0 and final['state']['gpuError'] is None,
          'Eight-edge campaign restores the original graph and connections')
    (ROOT/'evidence/uno-panel-edges-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':final['state']},indent=2))
    return checks
