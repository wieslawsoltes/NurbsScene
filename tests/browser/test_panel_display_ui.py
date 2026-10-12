"""Display-only panel menus, retained data and actual rendered preview text."""
import json
from pathlib import Path
from ui_observation import settle, read_ui, wait_for_ui
ROOT=Path(__file__).resolve().parents[2]

async def verify_panel_display_ui(page):
    checks=[]
    async def read():
        return await read_ui(page)
    async def wait(expression):
        return await wait_for_ui(page, expression)
    async def click(identity):
        data=await wait('!!globalThis.nurbsSceneUi.controls['+json.dumps(identity)+']');b=data['controls'][identity]
        if not b['enabled']:raise AssertionError('Disabled '+identity)
        await page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2);return await settle(page)
    async def node(identity):return next(n for n in (await read())['nodes'] if n['id']==identity)
    async def panel(identity):return next(p for p in (await read())['values']['panels'] if p['id']==identity)
    def check(value,name):
        if not value:raise AssertionError(name)
        checks.append(name);print('PANEL DISPLAY CHECK '+name,flush=True)
    async def toggle(identity,option):
        n=await node(identity)
        await page.mouse.click(n['x']+n['width']/2,n['y']+8,button='right');await settle(page)
        await click('GraphPanel_'+option)
        return await wait('globalThis.nurbsSceneUi.values.solvedRevision===globalThis.nurbsSceneUi.state.graphRevision')
    await click('GraphDockBottom');await click('GraphFit');initial=await settle(page)
    baseline=initial['state']['nodeCount'];ids={n['id'] for n in initial['nodes']}
    try:
        b=initial['controls']['GraphCanvas']
        await page.mouse.dblclick(b['x']+12,b['y']+12,delay=80);await wait('globalThis.nurbsSceneUi.state.searchOpen')
        await click('CanvasComponentSearch');await page.keyboard.insert_text('//'+('abcdefghijklmnopqrstuvwxyz'*4));await page.keyboard.press('Enter');await settle(page)
        data=await wait('!globalThis.nurbsSceneUi.state.searchOpen && globalThis.nurbsSceneUi.state.nodeCount==='+str(baseline+1))
        pid=next(n['id'] for n in data['nodes'] if n['id'] not in ids)
        await click('GraphFit');n=await node(pid)
        # Enlarge through the actual handle so the low-zoom text floor has room.
        x=n['x']+n['width']-3;y=n['y']+n['height']-3
        await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x+80,y+55,steps=8);await page.mouse.up();await settle(page)
        original=await panel(pid)
        await wait('globalThis.nurbsSceneUi.values.panels.some(p=>p.id==='+json.dumps(pid)+'&&p.drawnLines!==null)')
        before=await read();data=await toggle(pid,'DrawPaths');p=await panel(pid)
        check(not p['drawPaths'] and all(not line.startswith('{') for line in p['drawnLines']),
              'Draw paths menu hides branch headers in the actual rendered preview')
        check(data['values']['evaluated']==0 and p['items']==original['items'] and data['state']['graphPathBuilds']==before['state']['graphPathBuilds'],
              'Display-only menu preserves solved data and native wire paths')
        await toggle(pid,'DrawIndices');p=await panel(pid)
        check(p['drawIndices'] and any(line.startswith('0: ') for line in p['drawnLines']),
              'Draw indices menu labels actual panel items with zero-based indices')
        await toggle(pid,'WrapItems');p=await panel(pid)
        check(not p['wrapItems'] and p['drawnTruncated'] and any('…' in line for line in p['drawnLines']),
              'Disabling wrap clips long rendered items with a visible ellipsis')
        check(p['items']==original['items'],'Display clipping never truncates authoritative panel output')
        n=await node(pid);await page.mouse.click(n['x']+n['width']/2,n['y']+8);await settle(page)
        await page.keyboard.press('Control+D');await settle(page)
        d=await wait('globalThis.nurbsSceneUi.state.nodeCount==='+str(baseline+2))
        duplicate=next(p for p in d['values']['panels'] if p['id']!=pid)
        check(not duplicate['drawPaths'] and duplicate['drawIndices'] and not duplicate['wrapItems'] and duplicate['items']==original['items'],
              'Actual duplicate action preserves all panel display metadata and text')
        await click('GraphUndo');await wait('globalThis.nurbsSceneUi.state.nodeCount==='+str(baseline+1))
        n=await node(pid);await page.mouse.click(n['x']+n['width']/2,n['y']+8,button='right');await settle(page)
        await page.screenshot(path=str(ROOT/'evidence/uno-panel-display-menu.png'));await page.keyboard.press('Escape');await settle(page)
        await click('GraphUndo');p=await panel(pid);check(p['wrapItems'] and p['drawIndices'] and not p['drawPaths'],'One undo restores only the last display option')
        await click('GraphUndo');p=await panel(pid);check(not p['drawIndices'] and not p['drawPaths'],'Indices have an independent undo record')
        await click('GraphUndo');p=await panel(pid);check(p['drawPaths'] and not p['drawIndices'] and p['wrapItems'],'Undo restores the complete legacy display defaults')
        await click('GraphRedo');p=await panel(pid);check(not p['drawPaths'],'Redo reapplies the display-only edit')
        await click('GraphUndo');await click('GraphUndo');await click('GraphUndo')
        await click('GraphDockRight');await click('GraphFit');final=await settle(page)
        check(final['state']['nodeCount']==baseline and final['state']['graphErrors']==0 and final['state']['gpuError'] is None,
              'Panel display campaign restores the original definition without errors')
        (ROOT/'evidence/uno-panel-display-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':final['state']},indent=2))
    except Exception as error:
        (ROOT/'evidence/uno-panel-display-ui.json').write_text(json.dumps({'status':'failed','checks':checks,'error':str(error),'observed':await page.evaluate('globalThis.nurbsSceneUi')},indent=2));raise
    return checks
