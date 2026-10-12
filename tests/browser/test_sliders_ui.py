"""Real Uno slider dialog, exact apply/cancel, rounded scrubbing and history.
All application changes are performed through native pointer/keyboard input.
The qualification hook is observation-only.
"""
import json
from pathlib import Path
from ui_observation import settle, wait_for_ui, read_ui, text_editor_point
ROOT=Path(__file__).resolve().parents[2]

async def verify_sliders_ui(page):
    checks=[]
    async def read():
        return await read_ui(page)
    async def wait(expression):
        return await wait_for_ui(page, expression)
    async def click(identity):
        data=await wait('!!globalThis.nurbsSceneUi.controls['+json.dumps(identity)+']')
        b=data['controls'][identity]
        if not b['enabled']:raise AssertionError('Disabled '+identity)
        await page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2);await settle(page)
    async def text(identity,value):
        key=json.dumps(identity)
        data=await wait('!!globalThis.nurbsSceneUi.controls['+key+']')
        b=data['controls'][identity]
        if not b['enabled']:raise AssertionError('Disabled '+identity)
        await page.mouse.click(*text_editor_point(b))
        await wait('globalThis.nurbsSceneUi.controls['+key+'].focused')
        await page.keyboard.press('Control+A');await page.keyboard.insert_text(value)
        await wait('globalThis.nurbsSceneUi.controls['+key+'].text==='+json.dumps(value))
        await settle(page)
    async def node(identity):return next(n for n in (await read())['nodes'] if n['id']==identity)
    def check(ok,name):
        if not ok:raise AssertionError(name)
        checks.append(name);print('SLIDER CHECK '+name,flush=True)
    await click('GraphDockBottom');await click('GraphFit');await settle(page)
    data=await read();original=next(n for n in data['nodes'] if n['name']=='Rise');identity=original['id']
    at=(original['x']+original['width']/2,original['y']+2)
    revision=data['state']['graphRevision'];paths=data['state']['graphPathBuilds'];wire_passes=data['values']['wireCachePasses']
    await page.mouse.click(*at,button='right');await settle(page);await click('GraphEditSlider')
    await wait('globalThis.nurbsSceneUi.state.sliderEditorOpen')
    check((await read())['state']['graphRevision']==revision,'Context-menu slider editor opens without changing the definition')
    await text('SliderMinimum','1000');await text('SliderMaximum','10');await click('SliderApply')
    data=await wait('globalThis.nurbsSceneUi.state.sliderValidation.length>0')
    check(data['state']['sliderEditorOpen'] and data['state']['graphRevision']==revision,'Invalid range stays in the dialog with no partial graph edit')
    await text('SliderMinimum','-20');await text('SliderMaximum','80');await text('SliderValue','15.126');await text('SliderName','Rise edited')
    draft=(await read())['controls']
    check(all(draft[k]['text']==v for k,v in {'SliderMinimum':'-20','SliderMaximum':'80','SliderValue':'15.126','SliderName':'Rise edited'}.items()),
          'Native draft observation confirms every corrected field before Apply')
    await click('SliderPrecision');await page.keyboard.press('Home')
    for _ in range(3):await page.keyboard.press('ArrowDown')
    await page.keyboard.press('Enter');await settle(page)
    check((await read())['state']['sliderValidation']=='','Correcting the slider draft clears its obsolete validation message')
    await page.screenshot(path=str(ROOT/'evidence/uno-slider-dialog.png'))
    await click('SliderApply');await wait('!globalThis.nurbsSceneUi.state.sliderEditorOpen')
    current=await node(identity);data=await read()
    check(current['minimum']==-20 and current['maximum']==80 and current['decimalPlaces']==2 and abs(current['value']-15.13)<1e-10 and current['name']=='Rise edited',
          'Apply commits range, precision, rounded value and nickname together')
    check(data['state']['graphErrors']==0 and data['state']['graphPathBuilds']==paths,'Slider settings solve correctly without rebuilding wire paths')
    check(data['values']['wireCachePasses']==wire_passes,'Slider parameter edits skip native wire-cache reconciliation entirely')
    await click('GraphUndo');current=await node(identity)
    check(current['value']==original['value'] and current['name']==original['name'] and current['minimum']==original['minimum'] and current['maximum']==original['maximum'] and current['decimalPlaces']==original['decimalPlaces'],
          'One undo restores every original slider setting')
    await click('GraphRedo');current=await node(identity)
    check(current['value']==15.13 and current['name']=='Rise edited','Redo restores the complete slider edit')
    await click('GraphUndo');await click('GraphFit');await settle(page)
    n=await node(identity);data=await read();revision=data['state']['graphRevision']
    await page.mouse.dblclick(n['x']+n['width']/2,n['y']+2,delay=80);await settle(page)
    await wait('globalThis.nurbsSceneUi.state.sliderEditorOpen')
    check((await read())['state']['graphRevision']==revision,'Double-clicking the slider label opens settings without scrubbing its value')
    await text('SliderValue','9');await click('SliderCancel');data=await wait('!globalThis.nurbsSceneUi.state.sliderEditorOpen')
    check(data['state']['graphRevision']==revision and data['state']['graphRedo'] and (await node(identity))['value']==original['value'],
          'Cancel preserves the original value, revision and redo history')
    await click('GraphRedo');await click('GraphFit');await settle(page);n=await node(identity);data=await read();paths=data['state']['graphPathBuilds']
    await page.mouse.move(n['x']+n['width']*.45,n['y']+n['height']*.76);await page.mouse.down()
    await page.mouse.move(n['x']+n['width']*.67,n['y']+n['height']*.76,steps=8);await page.mouse.up();await settle(page)
    n=await node(identity);data=await read()
    check(abs(n['value']*100-round(n['value']*100))<1e-8 and n['value']!=15.13 and data['state']['graphPathBuilds']==paths,
          'Actual slider dragging follows the selected decimal precision and reuses paths')
    await click('GraphUndo');check((await node(identity))['value']==15.13,'A scrub gesture has one independent undo record')
    await click('GraphUndo');await click('GraphFit');await settle(page);n=await node(identity)
    await page.mouse.click(n['x']+n['width']/2,n['y']+2);await settle(page)
    await page.keyboard.press('Shift+F10');await settle(page);await click('GraphEditSlider');await wait('globalThis.nurbsSceneUi.state.sliderEditorOpen')
    check(True,'Keyboard context-menu entry opens the same slider editor')
    await page.keyboard.press('Escape');await wait('!globalThis.nurbsSceneUi.state.sliderEditorOpen')
    await click('GraphRedo');await click('GraphDockRight');await click('GraphFit');await settle(page)
    n=await node(identity);await page.mouse.click(n['x']+n['width']/2,n['y']+2);await settle(page)
    await click('GraphProperties');await wait('!!globalThis.nurbsSceneUi.controls.SelectedNodeValue')
    async def inspector_value(value):
        b=(await read())['controls']['SelectedNodeValue']
        await page.mouse.click(b['x']+b['width']*.3,b['y']+b['height']-12)
        await page.keyboard.press('Control+A');await page.keyboard.insert_text(value);await page.keyboard.press('Enter');await settle(page)
    # Math.Round uses ties-to-even: this input rounds to 12.34, not 12.35.
    await inspector_value('12.345');n=await node(identity);data=await read()
    check(n['value']==12.34 and data['state']['sliderInspectorValue']==12.34,'Native inspector commits a rounded value and displays the accepted number')
    revision=data['state']['graphRevision'];await inspector_value('999');data=await read()
    check((await node(identity))['value']==12.34 and data['state']['sliderInspectorValue']==12.34 and data['state']['graphRevision']==revision,
          'Rejected inspector value restores its display without changing graph history')
    await click('GraphUndo');check((await node(identity))['value']==15.13,'Inspector value editing has one independent undo record')
    await click('GraphProperties');await click('GraphUndo');await click('GraphFit');final=await settle(page)
    check(final['state']['graphErrors']==0 and final['state']['gpuError'] is None and (await node(identity))['value']==original['value'],
          'Slider editing leaves the original definition and GPU submission error-free')
    await page.screenshot(path=str(ROOT/'evidence/uno-sliders-final.png'))
    (ROOT/'evidence/uno-sliders-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':final['state']},indent=2))
    return checks
