"""Pointer/keyboard regressions against real Uno controls and Skia canvas.
The compile-time hook reports observed visual bounds/state only. Every tested
operation below uses mouse/keyboard input; the final flag only restores the test
session. No DOM replacement, mock GPU, or direct command invocation is used.
"""
import asyncio,json,math
from ui_observation import settle
from test_graph_editing_ui import verify_graph_editing_ui
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]

async def verify_workspace_ui(page):
    checks=[]
    async def observe():
        value=await page.evaluate('globalThis.nurbsSceneUi')
        if value.get('status')!='ready': raise AssertionError(value)
        return value
    async def wait(expression):
        await page.wait_for_function('globalThis.nurbsSceneUi?.status === "failed" || (globalThis.nurbsSceneUi?.status === "ready" && ('+expression+'))',timeout=20000)
        return await observe()
    async def click(identity,double=False):
        await wait('!!globalThis.nurbsSceneUi.controls['+json.dumps(identity)+']')
        data=await observe();box=data['controls'][identity]
        if not box['enabled']: raise AssertionError('Disabled control '+identity)
        await page.mouse.click(box['x']+box['width']/2,box['y']+box['height']/2,click_count=2 if double else 1,delay=75)
        await settle(page)
    async def command(text):
        await click('CommandInput');await page.keyboard.press('Control+A');await page.keyboard.insert_text(text);await page.keyboard.press('Enter')
    def check(ok,name):
        if not ok: raise AssertionError(name)
        checks.append(name)
    async def state(): return (await observe())['state']
    async def drag(a,b):
        await page.mouse.move(*a);await page.mouse.down();await page.mouse.move(*b,steps=9);await page.mouse.up();await settle(page)
    await wait('globalThis.nurbsSceneUi.state.nodeCount>0')
    initial=await state();objects=initial['objects'];node_count=initial['nodeCount']
    await page.screenshot(path=str(ROOT/'evidence/uno-ui-right.png'))
    try:
        await click('Toolbar_Curve');await wait('globalThis.nurbsSceneUi.state.toolbar==="Curve"')
        check('Tool_Circle' in (await observe())['controls'],'Toolbar category exposes executable modeling tools')
        await click('Tool_Circle');await page.keyboard.press('Control+A');await page.keyboard.insert_text('Circle 0,0,0 8');await page.keyboard.press('Enter')
        await wait(f'globalThis.nurbsSceneUi.state.objects==={objects+1}')
        check((await state())['lastName']=='Circle','Toolbar prompt and command keyboard create exact geometry')
        await click('Tool_Undo');await wait(f'globalThis.nurbsSceneUi.state.objects==={objects}')
        checks.append('Toolbar undo restores modeling edit')
        await click('GridSnap');await wait('globalThis.nurbsSceneUi.state.gridSnap')
        await click('Ortho');await wait('globalThis.nurbsSceneUi.state.ortho')
        await click('EndSnap');await wait('globalThis.nurbsSceneUi.state.endSnap')
        await click('EndSnap');await wait('!globalThis.nurbsSceneUi.state.endSnap')
        checks.append('Construction status toggles change the actual input settings')
        await click('Viewport_Top');await wait('globalThis.nurbsSceneUi.state.activeViewport==="Top"')
        check((await state())['maximized'] is None,'Single title click activates without maximizing')
        await page.wait_for_timeout(650);await click('Viewport_Top',double=True);await wait('globalThis.nurbsSceneUi.state.maximized==="Top"')
        await click('FourViewButton');await wait('globalThis.nurbsSceneUi.state.maximized===null')
        checks.append('Double-click title maximizes; four-view button restores')
        await click('Side_Line');await wait('globalThis.nurbsSceneUi.state.pending==="Line"')
        data=await observe();v=next(v for v in data['views'] if v['name']=='Top')
        await page.mouse.click(v['x']+v['width']*.29,v['y']+v['height']*.40)
        await wait('globalThis.nurbsSceneUi.state.anchor!==null')
        await page.mouse.click(v['x']+v['width']*.70,v['y']+v['height']*.66)
        await wait(f'globalThis.nurbsSceneUi.state.objects==={objects+1} && globalThis.nurbsSceneUi.state.pending===""')
        points=(await state())['lastPoints'];a,b=points
        check(abs(a[2])<1e-8 and abs(b[2])<1e-8 and all(abs(x-round(x))<1e-8 for x in a+b),'Pointer-created line obeys world grid snapping')
        check(abs(a[0]-b[0])<1e-8 or abs(a[1]-b[1])<1e-8,'Pointer-created line obeys orthographic construction constraint')
        await click('Tool_Undo');await wait(f'globalThis.nurbsSceneUi.state.objects==={objects}')
        before=await state();await click('Side_SelectAll');await wait(f'globalThis.nurbsSceneUi.state.selected==={objects}')
        await click('Side_SelectNone');await wait('globalThis.nurbsSceneUi.state.selected===0')
        after=await state();check(after['layerStructureChanges']==before['layerStructureChanges'],'Selection preserves existing layer rows')
        check(after['meshBuilds']==before['meshBuilds'],'Selection does not retessellate existing scene geometry')
        await click('GraphDockBottom');await wait('globalThis.nurbsSceneUi.state.dock==="Bottom"')
        await click('GraphFit');await page.screenshot(path=str(ROOT/'evidence/uno-ui-bottom.png'))
        data=await observe();check(data['controls']['GraphCanvas']['y']>data['controls']['CadCanvas']['y'],'Definition docking changes actual panel layout')
        await click('Category_Params');await click('Component_Number');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={node_count+1}')
        checks.append('Component shelf creates a real graph node')
        await click('GraphUndo');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={node_count}')
        await click('GraphRedo');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={node_count+1}')
        checks.append('Definition undo and redo restore added components')
        await click('GraphFit');data=await observe();added=next(n for n in data['nodes'] if n['component']=='Number' and n['name'] is None)
        before=await state();await page.mouse.click(added['x']+added['width']*.5,added['y']+6)
        await wait('globalThis.nurbsSceneUi.state.graphSelected.includes('+json.dumps(added['id'])+')')
        check((await state())['graphLayoutBuilds']==before['graphLayoutBuilds'],'Selecting a node reuses cached drawing layout')
        await drag((added['x']+added['width']*.5,added['y']+6),(added['x']+added['width']*.5+42,added['y']+27))
        await wait('globalThis.nurbsSceneUi.nodes.some(n=>n.id==='+json.dumps(added['id'])+' && n.modelX>'+str(added['modelX']+10)+')')
        await click('GraphUndo');await wait('globalThis.nurbsSceneUi.nodes.some(n=>n.id==='+json.dumps(added['id'])+' && Math.abs(n.modelX-'+str(added['modelX'])+')<1e-8)')
        checks.append('Node dragging commits one undoable position change')
        await click('GraphUndo');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={node_count}')
        await click('GraphFit');data=await observe();rise=next(n for n in data['nodes'] if n['name']=='Rise')
        await drag((rise['x']+rise['width']*.4,rise['y']+rise['height']*.75),(rise['x']+rise['width']*.8,rise['y']+rise['height']*.75))
        await wait('globalThis.nurbsSceneUi.nodes.some(n=>n.name==="Rise" && Math.abs(n.value-'+str(rise['value'])+')>1)')
        check((await state())['graphErrors']==0,'Compact slider scrubbing updates the real definition without errors')
        await click('GraphUndo');await wait('globalThis.nurbsSceneUi.nodes.some(n=>n.name==="Rise" && n.value==='+str(rise['value'])+')')
        checks.append('Slider gesture undo restores its prior value')
        before=await state()
        await click('Category_Curve');await click('Component_Circle');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={node_count+1}')
        await click('GraphFit');data=await observe()
        source=next(n for n in data['nodes'] if n['name']=='Width')['outputs'][0]
        target=next(p for n in data['nodes'] if n['component']=='Circle' for p in n['inputs'] if p['name']=='Radius')
        await drag((source['x'],source['y']),(target['x'],target['y']))
        await wait(f'globalThis.nurbsSceneUi.state.wireCount==={before["wireCount"]+1}')
        check((await state())['graphErrors']==0,'Dragging rendered ports creates a typed wire and solves without errors')
        await click('GraphUndo');await wait(f'globalThis.nurbsSceneUi.state.wireCount==={before["wireCount"]}')
        await click('GraphUndo');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={node_count}')
        checks.append('Wire and component edits undo independently')
        await page.keyboard.press('F8');await wait('!globalThis.nurbsSceneUi.state.ortho')
        await page.keyboard.press('F8');await wait('globalThis.nurbsSceneUi.state.ortho')
        await page.keyboard.press('F9');await wait('!globalThis.nurbsSceneUi.state.gridSnap')
        await page.keyboard.press('F9');await wait('globalThis.nurbsSceneUi.state.gridSnap')
        checks.append('Keyboard accelerators toggle the real construction constraints')
        await click('Category_Surface')
        before=await state();await click('NewLayer');await wait(f'globalThis.nurbsSceneUi.state.layers==={before["layers"]+1}')
        await click('Tool_Undo');await wait(f'globalThis.nurbsSceneUi.state.layers==={before["layers"]}')
        checks.append('Tabbed layer inspector creates a layer with document undo')
        await click('PanelProperties');await wait('globalThis.nurbsSceneUi.state.inspector==="Properties"')
        await click('PanelLayers');await wait('globalThis.nurbsSceneUi.state.inspector==="Layers"')
        checks.append('Property and layer tabs switch the native inspector')
        await click('GraphDockRight');await wait('globalThis.nurbsSceneUi.state.dock==="Right"')
        await click('GraphHide');await wait('!globalThis.nurbsSceneUi.state.definitionVisible')
        await click('Tool_Definition');await wait('globalThis.nurbsSceneUi.state.definitionVisible')
        checks.append('Definition can be hidden and restored from the common toolbar')
        await page.set_viewport_size({'width':820,'height':1050});await wait('globalThis.nurbsSceneUi.state.workspaceWidth<900')
        await page.wait_for_timeout(400);data=await observe()
        check(data['controls']['GraphCanvas']['y']>data['controls']['CadCanvas']['y'] and data['state']['dock']=='Right','Narrow workspace stacks panels without losing the chosen dock preference')
        box=data['controls']['GraphCanvas']
        check(all(n['x']>=box['x']-1 and n['y']>=box['y']-1 and n['x']+n['width']<=box['x']+box['width']+1 and n['y']+n['height']<=box['y']+box['height']+1 for n in data['nodes']),'Automatic dock change fits the complete definition in its new canvas')
        await page.screenshot(path=str(ROOT/'evidence/uno-ui-narrow.png'))
        await page.set_viewport_size({'width':1600,'height':1100});await wait('globalThis.nurbsSceneUi.state.workspaceWidth>1500')
        await click('GraphFit');await page.wait_for_timeout(300);data=await observe()
        check(data['controls']['GraphCanvas']['x']>data['controls']['CadCanvas']['x'],'Wide layout restores right-docked definition')
        check(data['state']['gpuError'] is None and data['state']['graphErrors']==0,'No GPU submission or component errors after pointer interactions')
        await page.screenshot(path=str(ROOT/'evidence/uno-ui-final.png'))
        checks.extend(await verify_graph_editing_ui(page))
        data=await observe()
        (ROOT/'evidence/uno-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':data['state']},indent=2))
    except Exception as error:
        data=await page.evaluate('globalThis.nurbsSceneUi')
        (ROOT/'evidence/uno-ui.json').write_text(json.dumps({'status':'failed','error':str(error),'checks':checks,'observed':data},indent=2))
        raise
    finally:
        await page.evaluate('globalThis.nurbsSceneUiFinish=true')
    await page.wait_for_function('globalThis.nurbsSceneUi?.status !== "ready"',timeout=15000)
    check((await page.evaluate('globalThis.nurbsSceneUi.status'))=='restored','Interaction test session restored')
    (ROOT/'evidence/uno-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':data['state']},indent=2))
    print(json.dumps({'suite':'real-uno-pointer-keyboard','passed':len(checks),'checks':checks},indent=2))
