"""Real panel resizing, draft isolation and multiline editor reopening."""
import json
from pathlib import Path
from ui_observation import settle
ROOT=Path(__file__).resolve().parents[2]

async def verify_panel_layout_ui(page):
    checks=[]
    async def read():
        data=await page.evaluate('globalThis.nurbsSceneUi')
        if data.get('status')!='ready':raise AssertionError(data)
        return data
    async def wait(expression):
        await page.wait_for_function('globalThis.nurbsSceneUi?.status!=="ready" || ('+expression+')',timeout=20000)
        return await read()
    async def click(identity):
        d=await wait('!!globalThis.nurbsSceneUi.controls['+json.dumps(identity)+']');b=d['controls'][identity]
        if not b['enabled']:raise AssertionError('Disabled '+identity)
        await page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2)
        return await settle(page)
    async def node(identity):return next(n for n in (await read())['nodes'] if n['id']==identity)
    async def create(query):
        before=await read();ids={n['id'] for n in before['nodes']};b=before['controls']['GraphCanvas']
        await page.mouse.dblclick(b['x']+12,b['y']+12,delay=80);await wait('globalThis.nurbsSceneUi.state.searchOpen')
        await click('CanvasComponentSearch');await page.keyboard.insert_text(query);await page.keyboard.press('Enter');await settle(page)
        d=await wait('!globalThis.nurbsSceneUi.state.searchOpen && globalThis.nurbsSceneUi.state.nodeCount==='+str(len(ids)+1))
        result=next(n for n in d['nodes'] if n['id'] not in ids);await click('GraphFit');return await node(result['id'])
    async def edit_text(value):
        await click('PanelTextInput');await page.keyboard.press('Control+A');await page.keyboard.insert_text(value)
        await click('PanelTextApply');await wait('!globalThis.nurbsSceneUi.values.editorOpen');await settle(page)
    async def open_panel(identity):
        n=await node(identity);await page.mouse.dblclick(n['x']+n['width']/2,n['y']+8,delay=80)
        await wait('globalThis.nurbsSceneUi.values.editorOpen');return await settle(page)
    def check(value,name):
        if not value:raise AssertionError(name)
        checks.append(name);print('PANEL LAYOUT CHECK '+name,flush=True)
    async def resize_start(n,dx,dy):
        x=n['x']+n['width']-3;y=n['y']+n['height']-3
        await page.mouse.move(x,y);await page.mouse.down();await page.mouse.move(x+dx,y+dy,steps=8)
        return await settle(page)
    await click('GraphDockBottom');await click('GraphFit');initial=await settle(page)
    baseline=initial['state']['nodeCount'];pid=None
    try:
        p=await create('//seed');pid=p['id'];await open_panel(pid)
        content='\n'.join('Line '+str(i) for i in range(16))+'\n'
        await edit_text(content);await click('GraphFit');p=await node(pid);before=await read()
        draft=await resize_start(p,90,65)
        check(draft['values']['resizeBounds'] is not None and draft['state']['graphRevision']==before['state']['graphRevision'],
              'Dragging a panel handle updates only a resize draft, not the graph')
        await page.screenshot(path=str(ROOT/'evidence/uno-panel-resize-draft.png'))
        await page.mouse.up();after=await settle(page);resized=await node(pid)
        check(resized['width']>p['width']+80 and resized['height']>p['height']+55 and after['values']['resizeBounds'] is None,
              'Release commits the expanded panel size to its actual canvas layout')
        check(after['state']['graphRevision']==before['state']['graphRevision']+1 and after['state']['graphErrors']==0,
              'Panel resize is one graph revision and preserves valid numerical outputs')
        await click('GraphUndo');restored=await node(pid)
        check(abs(restored['width']-p['width'])<.1 and abs(restored['height']-p['height'])<.1,
              'One undo restores the original panel dimensions')
        before=await read();await resize_start(restored,35,20);await page.keyboard.press('Escape');await page.mouse.up();after=await settle(page)
        check(after['values']['resizeBounds'] is None and after['state']['graphRevision']==before['state']['graphRevision'] and after['state']['graphRedo'],
              'Escape discards a resize draft without changing graph revision or redo')
        await click('GraphRedo');n=await node(pid)
        check(abs(n['width']-resized['width'])<.1 and abs(n['height']-resized['height'])<.1,
              'Redo restores the complete resized panel')
        before=await read();await page.mouse.click(n['x']+n['width']-3,n['y']+n['height']-3);after=await settle(page)
        check(after['state']['graphRevision']==before['state']['graphRevision'],
              'Clicking a resize handle without dragging is history-free')
        d=await open_panel(pid)
        check(d['values']['editorText']==content,'Reopening a resized panel retains every line and the trailing newline')
        revision=d['state']['graphRevision'];await click('PanelTextApply');d=await wait('!globalThis.nurbsSceneUi.values.editorOpen')
        check(d['state']['graphRevision']==revision,'Unchanged multiline Apply does not add a history entry')
        await settle(page);before=await read();n=await node(pid)
        await page.mouse.click(n['x']+n['width']/2,n['y']+8);d=await settle(page)
        check(d['values']['previewBuilds']==before['values']['previewBuilds'],
              'Selecting a resized panel reuses its formatted preview')
        await page.screenshot(path=str(ROOT/'evidence/uno-resized-panel.png'))
        # Legacy Text uses the same draft-only inspector editor instead of an
        # eagerly initialized multiline TextBox that truncated on focus loss.
        legacy=await create('Text');lid=legacy['id']
        await page.mouse.click(legacy['x']+legacy['width']/2,legacy['y']+8);await settle(page)
        await click('GraphProperties');await click('InspectorEditValue');await wait('globalThis.nurbsSceneUi.values.editorOpen')
        check((await read())['values']['editorText']=='NurbsScene','Legacy text inspector preserves its implicit default value')
        await edit_text('first\nsecond\n');await click('InspectorEditValue');d=await wait('globalThis.nurbsSceneUi.values.editorOpen')
        check(d['values']['editorText']=='first\nsecond\n','Legacy text inspector reopens multiline drafts without truncation')
        await click('PanelTextCancel');await wait('!globalThis.nurbsSceneUi.values.editorOpen');await click('GraphProperties')
        for _ in range(5):await click('GraphUndo')
        await click('GraphDockRight');await click('GraphFit');final=await settle(page)
        check(final['state']['nodeCount']==baseline and final['state']['graphErrors']==0 and final['state']['gpuError'] is None,
              'Panel layout workflows restore the original definition without graph or GPU errors')
        (ROOT/'evidence/uno-panel-layout-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':final['state']},indent=2))
    except Exception as error:
        (ROOT/'evidence/uno-panel-layout-ui.json').write_text(json.dumps({'status':'failed','error':str(error),'checks':checks,'observed':await read()},indent=2));raise
    return checks
