"""Actual Uno canvas creation, Boolean toggles and connected data panels.
Uses only native pointer/keyboard input; read hooks are bounded observations.
"""
import json
from pathlib import Path
from ui_observation import settle
ROOT=Path(__file__).resolve().parents[2]

async def verify_canvas_values_ui(page):
    checks=[]
    async def read():
        d=await page.evaluate('globalThis.nurbsSceneUi')
        if d.get('status')!='ready':raise AssertionError(d)
        return d
    async def wait(expr):
        await page.wait_for_function('globalThis.nurbsSceneUi?.status!=="ready" || ('+expr+')',timeout=20000)
        return await read()
    async def click(identity):
        d=await wait('!!globalThis.nurbsSceneUi.controls['+json.dumps(identity)+']');b=d['controls'][identity]
        if not b['enabled']:raise AssertionError('Disabled '+identity)
        await page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2);return await settle(page)
    async def text(identity,value):
        await click(identity);await page.keyboard.press('Control+A');await page.keyboard.insert_text(value);await settle(page)
    async def undo():
        await click('GraphUndo');await click('GraphFit');return await settle(page)
    async def node(identity):return next(n for n in (await read())['nodes'] if n['id']==identity)
    async def panel(identity):return next(n for n in (await read())['values']['panels'] if n['id']==identity)
    def check(value,name):
        if not value:raise AssertionError(name)
        checks.append(name);print('CANVAS CHECK '+name,flush=True)
    async def search(value):
        d=await read();b=d['controls']['GraphCanvas']
        await page.mouse.dblclick(b['x']+12,b['y']+12,delay=80);await wait('globalThis.nurbsSceneUi.state.searchOpen')
        await text('CanvasComponentSearch',value)
    async def create(value):
        before=await read();ids={n['id'] for n in before['nodes']}
        await search(value);await page.keyboard.press('Enter');await settle(page)
        d=await wait('!globalThis.nurbsSceneUi.state.searchOpen && globalThis.nurbsSceneUi.state.nodeCount==='+str(len(ids)+1))
        n=next(n for n in d['nodes'] if n['id'] not in ids)
        await click('GraphFit');return await node(n['id'])
    async def open_text(identity):
        n=await node(identity);await page.mouse.dblclick(n['x']+n['width']/2,n['y']+8,delay=80)
        await wait('globalThis.nurbsSceneUi.values.editorOpen');await settle(page)
    async def wire(source,source_port,target,target_port):
        a=next(p for p in (await node(source))['outputs'] if p['name']==source_port)
        b=next(p for p in (await node(target))['inputs'] if p['name']==target_port)
        await page.mouse.move(a['x'],a['y']);await page.mouse.down();await page.mouse.move(b['x'],b['y'],steps=10);await page.mouse.up();await settle(page)
    await click('GraphDockBottom');await click('GraphFit');baseline=await settle(page);count=baseline['state']['nodeCount'];wires=baseline['state']['wireCount']
    try:
        revision=baseline['state']['graphRevision'];await search('0<5<2');await page.keyboard.press('Enter');d=await settle(page)
        check(d['state']['searchOpen'] and d['state']['nodeCount']==count and d['state']['graphRevision']==revision and d['values']['error'],
              'Invalid numeric range leaves search open with a diagnostic and no partial creation')
        await text('CanvasComponentSearch','-20<5.25<80');await page.keyboard.press('Enter');d=await settle(page)
        await wait('!globalThis.nurbsSceneUi.state.searchOpen');n=next(n for n in d['nodes'] if n['id'] not in {n['id'] for n in baseline['nodes']})
        check(n['component']=='Number' and n['minimum']==-20 and n['maximum']==80 and n['value']==5.25 and n['decimalPlaces']==2,
              'Canvas range shorthand creates an initialized numeric slider')
        await undo();check((await read())['state']['nodeCount']==count,'One undo removes a shorthand creation')
        n=await create('+');check(n['component']=='Add','Operator search creates the executable Add component');await undo()
        n=await create('//seed');pid=n['id'];p=await panel(pid)
        check(n['component']=='Panel' and p['items']==['seed'],'Text shorthand creates a typed, evaluated data panel')
        await open_text(pid);await text('PanelTextInput','one\ntwo\n');await page.screenshot(path=str(ROOT/'evidence/uno-text-panel-dialog.png'))
        await click('PanelTextApply');await wait('!globalThis.nurbsSceneUi.values.editorOpen');p=await panel(pid)
        check(p['items']==['one','two',''] and p['itemCount']==3,'Native text editor preserves line items and the trailing empty row')
        await open_text(pid);revision=(await read())['state']['graphRevision'];await text('PanelTextInput','discard this')
        await click('PanelTextCancel');d=await wait('!globalThis.nurbsSceneUi.values.editorOpen')
        check(d['state']['graphRevision']==revision and (await panel(pid))['items']==['one','two',''],'Cancelling panel text editing preserves graph and output')
        await open_text(pid);await click('PanelSingleItem');await click('PanelTextApply');await wait('!globalThis.nurbsSceneUi.values.editorOpen')
        check((await panel(pid))['items']==['one\ntwo\n'],'Single-item mode retains multiline text in one typed item')
        await undo();check((await panel(pid))['itemCount']==3,'Panel mode is a single independently undoable edit')
        await undo();check((await panel(pid))['items']==['seed'],'Panel text has one independent undo record')
        boolean=await create('False');bid=boolean['id'];await wire(bid,'B',pid,'D');p=await panel(pid)
        check(p['items']==['False'],'Actual port dragging converts connected Boolean output into panel text')
        n=await node(bid);paths=(await read())['state']['graphPathBuilds']
        await page.mouse.dblclick(n['x']+n['width']/2,n['y']+8,delay=80);await settle(page)
        await wait('globalThis.nurbsSceneUi.nodes.some(n=>n.id==='+json.dumps(bid)+'&&n.value===true)');p=await panel(pid)
        check(p['items']==['True'] and (await read())['state']['graphPathBuilds']==paths,
              'Double-click toggles the real Boolean and connected panel without rebuilding wire paths')
        d=await settle(page);builds=d['values']['previewBuilds'];n=await node(pid)
        await page.mouse.click(n['x']+n['width']/2,n['y']+8);d=await settle(page)
        check(d['values']['previewBuilds']==builds,'Selecting a solved panel reuses its bounded draw-preview cache')
        await page.screenshot(path=str(ROOT/'evidence/uno-boolean-panel.png'))
        await undo();check((await panel(pid))['items']==['False'],'Boolean toggle has one undo record')
        await undo();check((await panel(pid))['items']==['seed'],'Disconnecting through undo restores stored panel text')
        await undo();check((await read())['state']['nodeCount']==count+1,'Undo removes the initialized Boolean without losing the panel')
        empty=await create('Merge');await wire(empty['id'],'R',pid,'D');p=await panel(pid)
        check(p['itemCount']==0 and p['branchCount']==0 and p['items']==[],
              'Connected empty data stays empty rather than displaying stale stored text')
        await undo();await undo();await undo()
        await click('GraphDockRight');await click('GraphFit');final=await settle(page)
        check(final['state']['nodeCount']==count and final['state']['wireCount']==wires and final['state']['graphErrors']==0 and final['state']['gpuError'] is None,
              'New canvas workflows restore the original definition with no graph or GPU error')
        (ROOT/'evidence/uno-canvas-values-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':final['state']},indent=2))
    except Exception as error:
        (ROOT/'evidence/uno-canvas-values-ui.json').write_text(json.dumps({'status':'failed','checks':checks,'error':str(error),'observed':await page.evaluate('globalThis.nurbsSceneUi')},indent=2));raise
    return checks
