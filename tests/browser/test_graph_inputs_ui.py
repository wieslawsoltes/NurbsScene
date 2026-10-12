"""Real Uno input/matching menus, bounded output previews and retained shelves."""
import json
from pathlib import Path
from ui_observation import settle
ROOT=Path(__file__).resolve().parents[2]

async def verify_graph_inputs_ui(page):
    checks=[]
    async def read():
        value=await page.evaluate('globalThis.nurbsSceneUi')
        if value.get('status')!='ready':raise AssertionError(value)
        return value
    async def wait(expression):
        await page.wait_for_function('globalThis.nurbsSceneUi?.status!=="ready" || ('+expression+')',timeout=20000)
        return await read()
    async def control(identity,hover=False):
        await wait('!!globalThis.nurbsSceneUi.controls['+json.dumps(identity)+']')
        box=(await read())['controls'][identity]
        if not box['enabled']:raise AssertionError('Disabled '+identity)
        at=(box['x']+box['width']/2,box['y']+box['height']/2)
        if hover:await page.mouse.move(*at)
        else:await page.mouse.click(*at)
        await settle(page)
    def check(ok,name):
        if not ok:raise AssertionError(name)
        checks.append(name);print('INPUT CHECK '+name,flush=True)
    async def node(name,context=False):
        data=await read();n=next(n for n in data['nodes'] if n['name']==name)
        await page.mouse.click(n['x']+n['width']/2,n['y']+5,button='right' if context else 'left');await settle(page)
        if context:await wait('globalThis.nurbsSceneUi.state.contextMenuOpen')
        return n
    await control('GraphDockBottom');await control('GraphFit')
    baseline=await read();count=baseline['state']['nodeCount']
    await node('Canopy',True);await control('GraphMatchingMenu',True);await control('GraphMatching_shortest')
    await wait('globalThis.nurbsSceneUi.nodes.some(n=>n.name==="Canopy"&&n.matching==="shortest")')
    check((await read())['state']['graphErrors']==0,'Native matching submenu changes the executable component mode')
    await control('GraphUndo');await wait('globalThis.nurbsSceneUi.nodes.some(n=>n.name==="Canopy"&&n.matching==="longest")')
    check(True,'Matching change has an independent graph undo')
    for transform in ['graft','flatten','simplify']:
        before=await read();ids=[w['id'] for w in before['wires']];paths=before['state']['graphPathBuilds']
        canopy=await node('Canopy',True)
        await control('GraphInputsMenu',True);await control('GraphInput_Width',True)
        if transform=='graft':await page.screenshot(path=str(ROOT/'evidence/uno-input-transform-menu.png'))
        await control('GraphInputTransform_Width_'+transform)
        await wait('globalThis.nurbsSceneUi.wires.some(w=>w.to==='+json.dumps(canopy['id'])+'&&w.toPort==="Width"&&w.transform==='+json.dumps(transform)+')')
        after=await settle(page)
        check(ids==[w['id'] for w in after['wires']] and after['state']['graphPathBuilds']==paths and after['state']['graphErrors']==0,
              'Input '+transform+' retains wire identities and native paths and solves')
        await control('GraphUndo');await wait('globalThis.nurbsSceneUi.wires.every(w=>w.transform==="none")')
    await node('Canopy');await control('GraphProperties')
    await wait('globalThis.nurbsSceneUi.previewLines.some(t=>t.includes("NURBS surface"))')
    data=await read();inspector=data['controls']['GraphInspector']
    await page.mouse.move(inspector['x']+inspector['width']/2,inspector['y']+inspector['height']/2);await page.mouse.wheel(0,1500);await settle(page)
    data=await read();preview=data['controls']['GraphResultPreview'];inspector=data['controls']['GraphInspector']
    check(preview['y']<inspector['y']+inspector['height'] and preview['y']+preview['height']>inspector['y'],'Scrolled inspector exposes the actual result preview')
    check(any('{0}' in t for t in data['previewLines']),'Inspector displays bounded path and exact-geometry metadata previews')
    await page.screenshot(path=str(ROOT/'evidence/uno-data-preview.png'))
    await control('GraphProperties');await control('GraphFit')
    width=await node('Width');before=await read()
    at=(width['x']+width['width']/2,width['y']+5)
    await page.mouse.move(*at);await page.mouse.down();await page.mouse.move(at[0]+.5,at[1]+.5);await page.mouse.up();await settle(page)
    after=await read();n=next(n for n in after['nodes'] if n['id']==width['id'])
    check(n['modelX']==width['modelX'] and n['modelY']==width['modelY'] and after['state']['graphRevision']==before['state']['graphRevision'],
          'Sub-threshold pointer jitter does not silently move a node or mutate the graph')
    await page.mouse.move(*at);await page.mouse.down();await page.mouse.move(at[0]+35,at[1]+10,steps=5);await settle(page)
    await wait('globalThis.nurbsSceneUi.nodes.some(n=>n.id==='+json.dumps(width['id'])+'&&n.modelX>'+str(width['modelX'])+')')
    await page.keyboard.press('Escape');await page.mouse.up();await settle(page)
    n=next(n for n in (await read())['nodes'] if n['id']==width['id'])
    check(n['modelX']==width['modelX'] and n['modelY']==width['modelY'],'Escape rolls back an in-progress node drag')
    await control('Category_Maths');await control('Category_Surface');before=await read()
    for category in ['Maths','Surface','Maths','Surface','Maths']:
        await control('Category_'+category)
    after=await read()
    check(after['state']['shelfPageBuilds']==before['state']['shelfPageBuilds'] and after['state']['shelfButtonBuilds']==before['state']['shelfButtonBuilds'],
          'Revisiting component categories reuses native shelf pages and buttons')
    await page.screenshot(path=str(ROOT/'evidence/uno-math-operator-shelf.png'))
    await control('Component_Add');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={count+1}')
    check(any(n['component']=='Add' for n in (await read())['nodes']),'Reused shelf button still creates its actual math component')
    await control('GraphUndo');await wait(f'globalThis.nurbsSceneUi.state.nodeCount==={count}')
    await control('Category_Surface');await control('GraphDockRight');await control('GraphFit')
    final=await read();check(final['state']['graphErrors']==0 and final['state']['gpuError'] is None,'Input-menu continuation leaves the definition and renderer error-free')
    await page.screenshot(path=str(ROOT/'evidence/uno-inputs-final.png'))
    (ROOT/'evidence/uno-inputs-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':final['state']},indent=2))
    return checks
