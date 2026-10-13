"""Native scrollbar input; all graph setup uses the actual canvas and editors."""
import json
from pathlib import Path
from ui_observation import read_ui, wait_for_ui, settle, text_editor_point
ROOT = Path(__file__).resolve().parents[2]

async def verify_panel_scrolling_ui(page):
    checks = []
    async def click(identity, editor=False):
        d = await wait_for_ui(page, '!!value.controls[' + json.dumps(identity) + ']')
        b = d['controls'][identity]
        if not b['enabled']: raise AssertionError('Disabled ' + identity)
        await page.mouse.click(*(text_editor_point(b) if editor else (b['x'] + b['width']/2, b['y'] + b['height']/2)))
        return await settle(page)
    def check(ok, name):
        if not ok: raise AssertionError(name)
        checks.append(name); print('PANEL SCROLL CHECK ' + name, flush=True)
        (ROOT/'evidence/uno-panel-scroll-progress.json').write_text(json.dumps({'checks': checks}, indent=2))
    await click('GraphDockBottom'); initial = await click('GraphFit'); canvas = initial['controls']['GraphCanvas']
    await page.mouse.click(canvas['x']+2, canvas['y']+2)
    await page.keyboard.press('Control+A'); await page.keyboard.press('Delete')
    await wait_for_ui(page, 'value.state.nodeCount===0')
    await page.mouse.dblclick(canvas['x']+canvas['width']/2, canvas['y']+30, delay=80)
    await wait_for_ui(page, 'value.state.searchOpen'); await click('CanvasComponentSearch')
    await page.keyboard.insert_text('//seed'); await page.keyboard.press('Enter')
    await wait_for_ui(page, 'value.state.nodeCount===1 && !value.state.searchOpen')
    d = await click('GraphFit'); pid = d['nodes'][0]['id']; key = json.dumps(pid)
    def panel(d): return next(p for p in d['values']['panels'] if p['id'] == pid)
    async def painted():
        return await wait_for_ui(page, 'value.values.solvedRevision===value.state.graphRevision && value.typography.panels.some(p=>p.id==='+key+'&&p.current)')
    async def title():
        d = await read_ui(page); n = next(n for n in d['nodes'] if n['id'] == pid)
        return n['x']+n['width']/2, n['y']+8
    def point(d, kind='Grip', fraction=.5):
        bar = panel(d)['scroll'][kind]; c = d['controls']['GraphCanvas']
        return c['x']+bar['X']+bar['Width']/2, c['y']+bar['Y']+bar['Height']*fraction
    await page.mouse.dblclick(*(await title()), delay=80)
    await wait_for_ui(page, 'value.values.editorOpen'); await click('PanelTextInput', editor=True)
    await wait_for_ui(page, 'value.controls.PanelTextInput.focused')
    await page.keyboard.press('Control+A')
    text = '\n'.join('item-'+str(i).zfill(6) for i in range(60))
    await page.keyboard.insert_text(text); await click('PanelTextApply')
    await wait_for_ui(page, '!value.values.editorOpen'); d = await painted(); p = panel(d)
    check(p['itemCount']==60 and p['scroll'] is not None and p['scroll']['Offset']==0 and p['scroll']['Total']==61,
          'Long panel exposes an actual item scrollbar including its branch header')
    # Create a real undoable move, then undo it to establish redo before scrolling.
    x,y = await title(); await page.mouse.move(x,y); await page.mouse.down(); await page.mouse.move(x+14,y+5,steps=5); await page.mouse.up(); await settle(page)
    await click('GraphUndo'); d = await painted()
    await page.mouse.click(*(await title())); d = await painted()
    baseline = {k:d['state'][k] for k in ('graphRevision','graphPathBuilds','graphErrors')}
    solved = d['values']['solvedRevision']; indexes = d['values']['panelIndexBuilds']
    check(d['state']['graphRedo'], 'Undo establishes real redo history before scrollbar interaction')
    await page.keyboard.press('End'); d = await painted(); p = panel(d)
    check(p['scroll']['Offset']==p['scroll']['Maximum'] and p['drawnLines'][-1]=='item-000059',
          'End reaches the final authoritative item without blank overscroll')
    await page.keyboard.press('Home'); d = await painted(); p = panel(d)
    check(p['scroll']['Offset']==0 and p['drawnLines'][0]=='{0}', 'Home restores the first branch and first data items')
    step = max(1,p['scroll']['Visible']-1)
    await page.keyboard.press('PageDown'); d = await painted()
    check(panel(d)['scroll']['Offset']==step, 'PageDown advances a visible item page with one-record overlap')
    await page.keyboard.press('PageUp'); d = await painted()
    check(panel(d)['scroll']['Offset']==0, 'PageUp returns to the prior item window')
    zoom = panel(d)['zoom']; await page.mouse.move(*point(d)); await page.mouse.wheel(0,120); d = await painted()
    check(panel(d)['scroll']['Offset']==3 and panel(d)['zoom']==zoom, 'Wheel over the scrollbar advances three items without zooming the canvas')
    await page.keyboard.press('Home'); d = await painted()
    x,y = point(d); target = point(d,'Track',1)[1]
    await page.mouse.move(x,y); await page.mouse.down(); await page.mouse.move(x,target,steps=10); await page.mouse.up(); d = await painted()
    check(panel(d)['scroll']['Offset']==panel(d)['scroll']['Maximum'] and panel(d)['drawnLines'][-1]=='item-000059',
          'Dragging the actual grip reaches the last item')
    await page.keyboard.press('Home'); d = await painted(); x,y = point(d); target = point(d,'Track',.65)[1]
    await page.mouse.move(x,y); await page.mouse.down(); await page.mouse.move(x,target,steps=8); d = await painted()
    check(panel(d)['scroll']['Offset']>0 and d['state']['graphRevision']==baseline['graphRevision'], 'Active grip drag changes only view state')
    await page.keyboard.press('Escape'); await page.mouse.up(); d = await painted()
    check(panel(d)['scroll']['Offset']==0, 'Escape restores the original scroll position without changing history')
    step = max(1,panel(d)['scroll']['Visible']-1)
    await page.mouse.click(*point(d,'Track',.8)); d = await painted()
    check(panel(d)['scroll']['Offset']==step, 'Clicking below the grip pages through the actual panel')
    check(all(d['state'][k]==v for k,v in baseline.items()) and d['values']['solvedRevision']==solved and d['state']['graphRedo'],
          'All scrolling preserves graph revision, solved output, native wires and redo')
    check(d['values']['panelIndexBuilds']==indexes, 'All repeated scroll operations retain the same source-tree index')
    n = next(n for n in d['nodes'] if n['id']==pid); zoom=panel(d)['zoom']; offset=panel(d)['scroll']['Offset']
    await page.mouse.move(n['x']+n['width']/2,n['y']+n['height']/2); await page.mouse.wheel(0,-120); d=await painted()
    check(panel(d)['zoom']!=zoom and panel(d)['scroll']['Offset']==offset, 'Wheel over panel text retains the original canvas zoom gesture')
    await page.keyboard.press('End'); d=await painted()
    await page.screenshot(path=str(ROOT/'evidence/uno-panel-scroll-end.png'))
    await click('GraphUndo'); d=await painted(); p=panel(d)
    check(p['items']==['seed'] and p['scroll'] is None, 'One undo changes the text, not a scrollbar gesture; short output removes the bar')
    await click('GraphUndo'); await wait_for_ui(page,'value.state.nodeCount===0')
    await click('GraphUndo'); await click('GraphDockRight'); final=await click('GraphFit')
    check(final['state']['nodeCount']==initial['state']['nodeCount'] and final['state']['graphErrors']==0 and final['state']['gpuError'] is None,
          'The scrollbar campaign restores the original workspace without graph or GPU errors')
    (ROOT/'evidence/uno-panel-scroll-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':final['state']},indent=2))
    return checks
