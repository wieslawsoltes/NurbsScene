"""Mandatory native-input test of ellipse tools, components and loft modeling.
Only observation hooks read exact geometry; no direct model commands are invoked.
"""
import json
from pathlib import Path
from ui_observation import read_ui, wait_for_ui, settle
ROOT=Path(__file__).resolve().parents[2]

async def verify_loft_ellipse_ui(page):
    checks=[]
    async def click(identity):
        d=await wait_for_ui(page,'!!globalThis.nurbsSceneUi.controls['+json.dumps(identity)+']')
        b=d['controls'][identity]
        if not b['enabled']:raise AssertionError('Disabled '+identity)
        await page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2)
        return await settle(page)
    async def command(text):
        await click('CommandInput')
        await page.keyboard.press('Control+A');await page.keyboard.insert_text(text);await page.keyboard.press('Enter')
        return await settle(page)
    def check(value,name):
        if not value:raise AssertionError(name)
        checks.append(name);print('GEOMETRY UI CHECK '+name,flush=True)
    original=await read_ui(page);count=original['state']['objects']
    # Empty the document by one undoable native command, retaining the graph.
    await command('SelAll');await command('Delete')
    await wait_for_ui(page,'globalThis.nurbsSceneUi.state.objects===0')
    await click('Toolbar_Curve')
    check('Tool_Ellipse' in (await read_ui(page))['controls'],'Curve toolbar exposes the authored Ellipse tool')
    await click('Tool_Ellipse')
    await page.keyboard.press('Control+A');await page.keyboard.insert_text('Ellipse 1,2,0 8 4');await page.keyboard.press('Enter')
    d=await settle(page);geometry=d['lastGeometry']
    check(d['state']['objects']==1 and d['state']['lastName']=='Ellipse' and geometry['kind']=='curve' and geometry['degreeU']==2 and geometry['countU']==9,
          'Ellipse toolbar prompt creates exact rational geometry, not a display polyline')
    for x,y,z in geometry['samples']:
        check(abs(((x-1)/8)**2+((y-2)/4)**2-1)<1e-8 and abs(z)<1e-9,'Created ellipse sample satisfies its analytic conic')
    await command('Ellipse 1,2,3 9 4.5');await command('Ellipse 1,2,6 10 5')
    await command('SelAll');d=await command('Loft')
    check(d['state']['objects']==4 and d['state']['lastName']=='Loft' and d['lastGeometry']['kind']=='surface' and
          d['lastGeometry']['countU']==9 and d['lastGeometry']['countV']==3,
          'Native Loft command interpolates the three ellipse sections')
    # At U=0,.5,1 the quarter-span geometry lies on the X extrema; V section
    # parameters 0,.5,1 must recover every original section exactly.
    for i,(x,y,z) in enumerate(d['lastGeometry']['samples']):
        row=i//3;sign=-1 if i%3==1 else 1
        check(abs(x-(1+sign*(8+row)))<1e-8 and abs(y-2)<1e-8 and abs(z-row*3)<1e-8,
              'Loft sample recovers its exact section and orientation')
    await command('ZoomExtents');await command('SelNone')
    check((await read_ui(page))['state']['gpuError'] is None,'Ellipse loft is submitted through the existing real viewport renderer')
    await page.screenshot(path=str(ROOT/'evidence/uno-ellipse-loft.png'))
    await command('Undo');check((await read_ui(page))['state']['objects']==3,'Loft has one document undo record')
    await command('Redo');check((await read_ui(page))['state']['objects']==4,'Document redo restores the exact loft')
    await command('Undo')
    for _ in range(3):await command('Undo')
    await command('Undo') # Restore document deleted at campaign entry.
    check((await read_ui(page))['state']['objects']==count,'Modeling campaign restores the original document')
    # Exercise real component search and baking without inspecting output via a
    # private mutation API.
    await click('GraphDockBottom');await click('GraphFit');d=await read_ui(page);box=d['controls']['GraphCanvas']
    await page.mouse.dblclick(box['x']+12,box['y']+12,delay=80)
    await wait_for_ui(page,'globalThis.nurbsSceneUi.state.searchOpen')
    await click('CanvasComponentSearch');await page.keyboard.insert_text('Ellipse');await page.keyboard.press('Enter')
    d=await settle(page)
    check(d['state']['nodeCount']==original['state']['nodeCount']+1 and any(n['component']=='Ellipse' for n in d['nodes']) and d['state']['graphErrors']==0,
          'Canvas search creates and solves the real Ellipse component')
    d=await click('GraphBake')
    check(d['state']['objects']==count+1 and d['lastGeometry']['kind']=='curve' and d['lastGeometry']['degreeU']==2,
          'Ellipse component bakes exact reusable geometry')
    await command('Undo');await click('GraphUndo');await click('GraphDockRight');await click('GraphFit');d=await read_ui(page)
    check(d['state']['objects']==count and d['state']['nodeCount']==original['state']['nodeCount'] and
          d['state']['graphErrors']==0 and d['state']['gpuError'] is None,
          'Ellipse component and baking undo leave the original workspace valid')
    (ROOT/'evidence/uno-loft-ellipse-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':d['state']},indent=2))
    return checks
