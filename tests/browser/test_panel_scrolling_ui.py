"""Native scrollbar input; all graph setup uses the actual canvas and editors."""
import json
from pathlib import Path
from ui_observation import read_ui, wait_for_ui, settle, text_editor_point, wait_for_solved_ui, scroll_condition
ROOT = Path(__file__).resolve().parents[2]

async def verify_panel_scrolling_ui(page):
    checks = []
    last_painted = None
    async def click(identity, editor=False):
        d = await wait_for_ui(page, '!!value.controls[' + json.dumps(identity) + ']')
        b = d['controls'][identity]
        if not b['enabled']: raise AssertionError('Disabled ' + identity)
        await page.mouse.click(*(text_editor_point(b) if editor else (b['x'] + b['width']/2, b['y'] + b['height']/2)))
        return await settle(page)
    def check(ok, name):
        if not ok:
            (ROOT/'evidence/uno-panel-scroll-assertion.json').write_text(json.dumps({'check': name, 'snapshot': last_painted}, indent=2))
            raise AssertionError(name)
        checks.append(name); print('PANEL SCROLL CHECK ' + name, flush=True)
        (ROOT/'evidence/uno-panel-scroll-progress.json').write_text(json.dumps({'checks': checks, 'asserted': last_painted}, indent=2))
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
    async def painted(after=None, active=None):
        # Native event acknowledgement and completed paint are separate from
        # expected output. Incorrect completed windows reach the assertions.
        nonlocal last_painted
        last_painted = await wait_for_ui(page, scroll_condition(pid, after_sequence=after, active=active))
        return last_painted
    async def scroll_key(key):
        before = await read_ui(page)
        await page.keyboard.press(key)
        return await painted(before['values']['panelScrollSequence'], False)
    async def scroll_wheel(delta, at):
        before = await read_ui(page)
        await page.mouse.move(*at); await page.mouse.wheel(0, delta)
        return await painted(before['values']['panelScrollSequence'], False)
    async def title():
        d = await read_ui(page); n = next(n for n in d['nodes'] if n['id'] == pid)
        return n['x']+n['width']/2, n['y']+8
    def point(d, kind='Grip', fraction=.5):
        bar = panel(d)['scroll'][kind]; c = d['controls']['GraphCanvas']
        return c['x']+bar['X']+bar['Width']/2, c['y']+bar['Y']+bar['Height']*fraction
    await page.mouse.dblclick(*(await title()), delay=80)
    await wait_for_ui(page, 'value.values.editorOpen'); await click('PanelTextInput', editor=True)
    editing = await wait_for_ui(page, 'value.controls.PanelTextInput.focused')
    check(not editing['controls']['GraphCanvas']['focused'] and editing['state']['graphFocus']=='Unfocused',
          'A focused text editor does not falsely report its graph canvas as focused')
    await page.keyboard.press('Control+A')
    text = '\n'.join('item-'+str(i).zfill(6) for i in range(60))
    await page.keyboard.insert_text(text); before = await read_ui(page); await click('PanelTextApply')
    await wait_for_solved_ui(page, editor_closed=True, after_revision=before['state']['graphRevision']); d = await painted(); p = panel(d)
    check(p['itemCount']==60 and p['scroll'] is not None and p['scroll']['Offset']==0 and p['scroll']['Total']==61,
          'Long panel exposes an actual item scrollbar including its branch header')
    # Create a real undoable move, then undo it to establish redo before scrolling.
    x,y = await title(); await page.mouse.move(x,y); await page.mouse.down(); await page.mouse.move(x+14,y+5,steps=5); await page.mouse.up(); await settle(page)
    await click('GraphUndo'); d = await painted()
    await page.mouse.click(*(await title())); await settle(page); d = await painted()
    baseline = {k:d['state'][k] for k in ('graphRevision','graphPathBuilds','graphErrors')}
    solved = d['values']['solvedRevision']; indexes = d['values']['panelIndexBuilds']
    check(d['state']['graphRedo'], 'Undo establishes real redo history before scrollbar interaction')
    focused = await wait_for_ui(page, 'value.controls.GraphCanvas.focused')
    check(focused['state']['graphFocus']!='Unfocused' and focused['state']['focusedElement']=='NurbsScene.Controls.GraphCanvas',
          'Canvas focus observation agrees with native focus state and root-local identity')
    d = await scroll_key('End'); p = panel(d)
    check(p['scroll']['Offset']==p['scroll']['Maximum'] and p['drawnLines'][-1]=='item-000059',
          'End reaches the final authoritative item without blank overscroll')
    d = await scroll_key('Home'); p = panel(d)
    check(p['scroll']['Offset']==0 and p['drawnLines'][0]=='{0}', 'Home restores the first branch and first data items')
    step = max(1,p['scroll']['Visible']-1)
    d = await scroll_key('PageDown')
    check(panel(d)['scroll']['Offset']==step, 'PageDown advances a visible item page with one-record overlap')
    d = await scroll_key('PageUp')
    check(panel(d)['scroll']['Offset']==0, 'PageUp returns to the prior item window')
    zoom = panel(d)['zoom']; d = await scroll_wheel(120, point(d))
    check(panel(d)['scroll']['Offset']==3 and panel(d)['zoom']==zoom, 'Wheel over the scrollbar advances three items without zooming the canvas')
    d = await scroll_key('Home')
    x,y = point(d); target = point(d,'Track',1)[1]
    sequence = d['values']['panelScrollSequence']
    await page.mouse.move(x,y); await page.mouse.down(); await page.mouse.move(x,target,steps=10); await page.mouse.up(); d = await painted(sequence, False)
    check(panel(d)['scroll']['Offset']==panel(d)['scroll']['Maximum'] and panel(d)['drawnLines'][-1]=='item-000059',
          'Dragging the actual grip reaches the last item')
    d = await scroll_key('Home'); x,y = point(d); target = point(d,'Track',.65)[1]
    sequence = d['values']['panelScrollSequence']
    await page.mouse.move(x,y); await page.mouse.down(); await page.mouse.move(x,target,steps=8); await settle(page); d = await painted(sequence, True)
    check(panel(d)['scroll']['Offset']>0 and d['state']['graphRevision']==baseline['graphRevision'], 'Active grip drag changes only view state')
    sequence = d['values']['panelScrollSequence']
    await page.keyboard.press('Escape'); await page.mouse.up(); d = await painted(sequence, False)
    check(panel(d)['scroll']['Offset']==0, 'Escape restores the original scroll position without changing history')
    step = max(1,panel(d)['scroll']['Visible']-1)
    sequence = d['values']['panelScrollSequence']
    await page.mouse.click(*point(d,'Track',.8)); d = await painted(sequence, False)
    check(panel(d)['scroll']['Offset']==step, 'Clicking below the grip pages through the actual panel')
    check(all(d['state'][k]==v for k,v in baseline.items()) and d['values']['solvedRevision']==solved and d['state']['graphRedo'],
          'All scrolling preserves graph revision, solved output, native wires and redo')
    check(d['values']['panelIndexBuilds']==indexes, 'All repeated scroll operations retain the same source-tree index')
    d = await scroll_key('Home'); x,y = point(d); target = point(d,'Track',1)[1]
    sequence = d['values']['panelScrollSequence']
    await page.mouse.move(x,y); await page.mouse.down(); await page.mouse.move(x,(y+target)/2,steps=4)
    await page.mouse.down(button='right'); await page.mouse.up(button='right')
    await page.mouse.move(x,target,steps=4); await page.mouse.up(); d = await painted(sequence, False)
    check(panel(d)['scroll']['Offset']==panel(d)['scroll']['Maximum'] and not d['state']['contextMenuOpen'],
          'Secondary-button release cannot commit or replace the owning scrollbar drag')
    zoom = panel(d)['zoom']; d = await scroll_wheel(-12000, point(d))
    check(panel(d)['scroll']['Offset']==0 and panel(d)['zoom']==zoom,
          'Large scrollbar wheel input saturates at Home without zoom or overflow')
    d = await scroll_wheel(12000, point(d))
    check(panel(d)['scroll']['Offset']==panel(d)['scroll']['Maximum'] and d['values']['panelIndexBuilds']==indexes,
          'Large scrollbar wheel input reaches End without rebuilding the source index')
    n = next(n for n in d['nodes'] if n['id']==pid); zoom=panel(d)['zoom']; offset=panel(d)['scroll']['Offset']
    await page.mouse.move(n['x']+n['width']/2,n['y']+n['height']/2); await page.mouse.wheel(0,-120); await settle(page); d=await painted()
    check(panel(d)['zoom']!=zoom and panel(d)['scroll']['Offset']==offset, 'Wheel over panel text retains the original canvas zoom gesture')
    d=await scroll_key('End')
    await page.screenshot(path=str(ROOT/'evidence/uno-panel-scroll-end.png'))
    await click('GraphUndo'); d=await painted(); p=panel(d)
    check(p['items']==['seed'] and p['scroll'] is None, 'One undo changes the text, not a scrollbar gesture; short output removes the bar')
    await click('GraphUndo'); await wait_for_ui(page,'value.state.nodeCount===0')
    await click('GraphUndo'); await click('GraphDockRight'); final=await click('GraphFit')
    check(final['state']['nodeCount']==initial['state']['nodeCount'] and final['state']['graphErrors']==0 and final['state']['gpuError'] is None,
          'The scrollbar campaign restores the original workspace without graph or GPU errors')
    (ROOT/'evidence/uno-panel-scroll-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':final['state']},indent=2))
    return checks
