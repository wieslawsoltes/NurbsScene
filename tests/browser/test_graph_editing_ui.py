"""Real pointer/keyboard coverage for graph menus, fragments and incremental paths.
No application mutation is invoked through the observation hook.
"""
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]

async def verify_graph_editing_ui(page):
    checks=[]
    async def read():
        value=await page.evaluate('globalThis.nurbsSceneUi')
        if value.get('status')!='ready':raise AssertionError(value)
        return value
    async def wait(expression):
        await page.wait_for_function('globalThis.nurbsSceneUi?.status==="failed" || (globalThis.nurbsSceneUi?.status==="ready" && ('+expression+'))',timeout=20000)
        return await read()
    async def click(identity):
        await wait('!!globalThis.nurbsSceneUi.controls['+json.dumps(identity)+']')
        box=(await read())['controls'][identity]
        if not box['enabled']:raise AssertionError('Disabled '+identity)
        await page.mouse.click(box['x']+box['width']/2,box['y']+box['height']/2)
        await page.wait_for_timeout(180)
    async def key(value):
        await page.keyboard.press(value);await page.wait_for_timeout(180)
    async def focus_node(name):
        data=await read();node=next(n for n in data['nodes'] if n['name']==name)
        await page.mouse.click(node['x']+node['width']*.5,node['y']+5)
        await page.wait_for_timeout(150);return node
    async def drag(a,b,button='left'):
        await page.mouse.move(*a);await page.mouse.down(button=button);await page.mouse.move(*b,steps=10);await page.mouse.up(button=button);await page.wait_for_timeout(250)
    def check(value,name):
        if not value:raise AssertionError(name)
        checks.append(name)
        print("EDIT CHECK "+name,flush=True)
    await click('GraphDockBottom');await click('GraphFit');await page.wait_for_timeout(250)
    baseline=await read();count=baseline['state']['nodeCount'];wires=baseline['state']['wireCount']
    old_ids={n['id'] for n in baseline['nodes']}
    await focus_node('Width');await key('Control+A')
    await wait(f'globalThis.nurbsSceneUi.state.graphSelected.length==={count}')
    checks.append('Ctrl+A selects the complete definition without altering it')
    await key('Control+D');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={count*2} && globalThis.nurbsSceneUi.state.wireCount==={wires*2}')
    duplicate=await read();selected=set(duplicate['state']['graphSelected'])
    check(len(selected)==count and not selected.intersection(old_ids),'Duplicate assigns fresh node identities and selects the new fragment')
    await key('Control+Z');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={count}')
    checks.append('One keyboard undo removes the entire duplicated fragment')
    await key('Control+A');await key('Control+C');await key('Control+V')
    await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={count*2} && globalThis.nurbsSceneUi.state.wireCount==={wires*2}')
    paste=await read();selected=set(paste['state']['graphSelected'])
    check(len(selected)==count and not selected.intersection(old_ids),'Keyboard copy/paste retains internal connections under new identities')
    await key('Control+X');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={count}')
    await key('Control+Z');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={count*2}')
    await key('Control+Z');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={count}')
    checks.append('Cut and paste remain separate atomic undo actions')
    await click('GraphFit');node=await focus_node('Canopy')
    await page.mouse.click(node['x']+node['width']/2,node['y']+8,button='right')
    await wait('globalThis.nurbsSceneUi.state.contextMenuOpen')
    await page.screenshot(path=str(ROOT/'evidence/uno-edit-context.png'))
    await click('GraphToggle_Preview')
    await wait('globalThis.nurbsSceneUi.nodes.some(n=>n.name==="Canopy" && !n.preview)')
    checks.append('Component context menu changes actual preview state')
    await click('GraphUndo');await wait('globalThis.nurbsSceneUi.nodes.some(n=>n.name==="Canopy" && n.preview)')
    await focus_node('Width');await key('Control+A');await click('GraphMenu_Arrange');await click('GraphArrange_Top')
    await wait('new Set(globalThis.nurbsSceneUi.nodes.map(n=>n.modelY)).size===1')
    checks.append('Native Arrange menu aligns the real node positions')
    await click('GraphUndo');await click('GraphFit')
    data=await read();box=data['controls']['GraphCanvas']
    (ROOT/'evidence/marquee-before.json').write_text(json.dumps(data,indent=2))
    await drag((box['x']+6,box['y']+6),(box['x']+box['width']-6,box['y']+box['height']-6))
    await wait(f'globalThis.nurbsSceneUi.state.graphSelected.length==={count}')
    checks.append('Left-drag window selection selects enclosed components')
    node=next(n for n in (await read())['nodes'] if n['name']=='Width')
    await page.keyboard.down('Shift');await page.mouse.click(node['x']+node['width']/2,node['y']+5);await page.keyboard.up('Shift')
    await wait(f'globalThis.nurbsSceneUi.state.graphSelected.length==={count-1}')
    checks.append('Shift-click removes an already selected node')
    await drag((box['x']+8,box['y']+8),(box['x']+55,box['y']+30),button='right')
    check(not (await read())['state']['contextMenuOpen'],'Right-drag pans without opening the context menu')
    await click('GraphFit');data=await read();rise=next(n for n in data['nodes'] if n['name']=='Rise');paths=data['state']['graphPathBuilds']
    await drag((rise['x']+rise['width']*.4,rise['y']+rise['height']*.75),(rise['x']+rise['width']*.8,rise['y']+rise['height']*.75))
    await wait('globalThis.nurbsSceneUi.nodes.some(n=>n.name==="Rise" && Math.abs(n.value-'+str(rise['value'])+')>1)')
    check((await read())['state']['graphPathBuilds']==paths,'Slider parameter changes reuse every cached native wire path')
    await click('GraphUndo');await click('GraphFit')
    for attempt in range(2):
        data=await read();box=data['controls']['GraphCanvas'];at=(box['x']+12,box['y']+12)
        await page.mouse.dblclick(*at,delay=80);await wait('globalThis.nurbsSceneUi.state.searchOpen')
        await click('CanvasComponentSearch');await page.keyboard.insert_text('Number')
        await page.screenshot(path=str(ROOT/f'evidence/uno-canvas-search-{attempt}.png'))
        await page.keyboard.press('Enter');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={count+1} && !globalThis.nurbsSceneUi.state.searchOpen')
        checks.append('Canvas search adds an executable component'+(' after reopening' if attempt else ''))
        if attempt==0:
            await click('CommandInput');await page.keyboard.insert_text('draft text');await key('Control+Z')
            check((await read())['state']['nodeCount']==count+1,'Text-input undo does not execute graph undo')
            await page.keyboard.press('Control+A');await page.keyboard.press('Backspace')
        await click('GraphUndo');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={count}');await click('GraphFit');await page.wait_for_timeout(650)
    await click('GraphDockRight');await click('GraphFit');await page.wait_for_timeout(250)
    final=await read();check(final['state']['graphErrors']==0 and final['state']['gpuError'] is None,'Editing leaves the definition and GPU submission error-free')
    await page.screenshot(path=str(ROOT/'evidence/uno-editing-final.png'))
    (ROOT/'evidence/uno-editing-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':final['state']},indent=2))
    return checks
