"""Rhino-style modeling shell acceptance against the actual Uno publication.

Only native pointer/keyboard input changes the application. Observed control
bounds, drafts, camera values and document state are read-only. No command hook,
DOM replacement, forced focus, skipped action or retry-to-pass is used.
"""
import json
import math
from pathlib import Path
from ui_observation import read_ui, settle, text_editor_point, wait_for_ui, wait_ready

ROOT = Path(__file__).resolve().parents[2]


def projected_anchor(before, after, rx, ry, width, height):
    """Project a point on the original Perspective target plane after navigation."""
    def basis(c):
        az, el = c['azimuth'], c['elevation']
        right = (-math.sin(az), math.cos(az), 0)
        up = (-math.sin(el)*math.cos(az), -math.sin(el)*math.sin(az), math.cos(el))
        offset = (math.cos(el)*math.cos(az), math.cos(el)*math.sin(az), math.sin(el))
        return right, up, offset
    right, up, _ = basis(before)
    extent = 2*before['distance']*math.tan(before['fov']/2)
    point = [before['target'][i] + right[i]*(rx-.5)*extent*width/height + up[i]*(.5-ry)*extent for i in range(3)]
    right, up, offset = basis(after)
    d = [point[i]-after['target'][i]-after['distance']*offset[i] for i in range(3)]
    depth = -sum(d[i]*offset[i] for i in range(3))
    extent = 2*depth*math.tan(after['fov']/2)
    return ((sum(d[i]*right[i] for i in range(3))/(extent*width/height)+.5)*width,
            (.5-sum(d[i]*up[i] for i in range(3))/extent)*height)


async def verify_rhino_workspace(page):
    checks = []
    async def state(): return (await read_ui(page))['state']
    async def click(identity, *, double=False):
        data = await wait_for_ui(page, '!!value.controls['+json.dumps(identity)+']')
        box = data['controls'][identity]
        if not box['enabled']:
            raise AssertionError('Disabled native control: '+identity)
        await page.mouse.click(box['x']+box['width']/2, box['y']+box['height']/2,
                               click_count=2 if double else 1, delay=75)
        return await settle(page)
    async def command(text):
        await click('CommandInput')
        await page.keyboard.press('Control+A')
        await page.keyboard.insert_text(text)
        await page.keyboard.press('Enter')
        return await settle(page)
    async def view(name):
        return next(v for v in (await read_ui(page))['views'] if v['name']==name)
    async def camera(name='Perspective'): return (await state())['cameras'][name]
    async def editor(identity, text):
        data = await wait_for_ui(page, '!!value.controls['+json.dumps(identity)+']')
        await page.mouse.click(*text_editor_point(data['controls'][identity]))
        focus = await settle(page)
        if not focus['controls'][identity]['focused']:
            raise AssertionError('Native camera editor did not receive focus: '+identity)
        await page.keyboard.press('Control+A'); await page.keyboard.insert_text(text)
        data = await settle(page)
        if data['controls'][identity]['text'] != text:
            raise AssertionError('Camera edit landed in the wrong native field: '+identity)
    def check(ok, name):
        if not ok: raise AssertionError(name)
        checks.append(name)
    async def gesture(name, *, button='right', modifier=None, dx=50, dy=25, cancel=False):
        v = await view(name)
        start = (v['x']+v['width']*.42, v['y']+v['height']*.43)
        if modifier: await page.keyboard.down(modifier)
        try:
            await page.mouse.move(*start); await page.mouse.down(button=button)
            await page.mouse.move(start[0]+dx, start[1]+dy, steps=8)
            during = await settle(page)
            if cancel: await page.keyboard.press('Escape')
            await page.mouse.up(button=button)
        finally:
            if modifier: await page.keyboard.up(modifier)
        return during, await settle(page), v

    initial = await state()
    objects = initial['objects']
    try:
        # The qualification fixture deliberately opens the definition, but the
        # application itself starts empty and defaults to the modeling layout.
        model_report = await page.evaluate('globalThis.nurbsSceneQualification')
        check(any('Ordinary startup is an empty modeling document' in c for c in model_report['checks']),
              'Ordinary application startup is empty before the explicit qualification fixture')
        data = await command('ModelingLayout')
        check(not data['state']['definitionVisible'] and data['state']['inspector']=='Properties'
              and not data['state']['viewportPropertyDraft'],
              'Modeling layout gives the model space, opens clean viewport Properties and leaves no synthetic draft')
        check(data['controls']['CadCanvas']['width']>1200 and len(data['views'])==4,
              'Modeling layout renders four usable full-width native viewports')
        check(data['state']['documentRevision']==initial['documentRevision'] and data['state']['objects']==objects,
              'Changing workspace layout does not edit geometry or consume document undo')
        await page.screenshot(path=str(ROOT/'evidence/uno-rhino-modeling.png'))

        compact = data['controls']['CommandHistory']['height']
        await page.keyboard.press('F2'); data=await settle(page)
        check(data['state']['historyExpanded'] and data['controls']['CommandHistory']['height']>compact+150
              and data['state']['commandInput']=='',
              'F2 expands selectable command history and submitted command input remains clear')
        await click('CommandHistory'); await page.keyboard.press('Control+A'); await page.keyboard.press('Control+C')
        await page.wait_for_timeout(100)
        copied=await page.evaluate('navigator.clipboard.readText()')
        check('ModelingLayout' in copied and 'NurbsScene' in copied,
              'Native read-only command history supports selecting and copying its text')
        await page.screenshot(path=str(ROOT/'evidence/uno-rhino-command-history.png'))
        await page.keyboard.press('F2'); data=await settle(page)
        check(not data['state']['historyExpanded'] and data['controls']['CommandHistory']['height']==compact,
              'F2 restores compact command history without clearing it')

        # Type using real key events on the model canvas, not insert_text or a
        # direct command API: the initial character must not be lost or doubled.
        v=await view('Top')
        await page.mouse.click(v['x']+v['width']*.88,v['y']+v['height']*.14)
        await page.keyboard.type('Circle 0,0,0 6',delay=45)
        data=await settle(page)
        check(data['state']['commandInput']=='Circle 0,0,0 6',
              'Typing on the model canvas transfers every character exactly once to command input')
        await page.keyboard.press('Enter'); data=await settle(page)
        check(data['state']['objects']==objects+1 and data['state']['lastName']=='Circle',
              'Canvas-initiated command entry creates an exact model curve')
        await command('CommandHistory')
        await click('CommandInput'); await page.keyboard.press('Enter'); data=await settle(page)
        check(data['state']['objects']==objects+2,
              'Blank Enter repeats the last modeling command, not the intervening history command')
        await page.keyboard.press('F2'); await settle(page)
        v=await view('Perspective')
        p=(v['x']+v['width']*.84,v['y']+v['height']*.15)
        await page.mouse.click(*p,button='right',delay=75); data=await settle(page)
        check(data['state']['objects']==objects+3 and not data['state']['modelContextMenuOpen'],
              'A short right click repeats the last modeling command without opening a context menu')
        await page.keyboard.press('Space'); data=await settle(page)
        check(data['state']['objects']==objects+4,'Space on the focused model canvas repeats the last modeling command')
        await page.keyboard.press('Control+z'); data=await settle(page)
        check(data['state']['objects']==objects+3,'Control+Z on the model canvas undoes a model operation')
        await page.keyboard.press('Control+y'); data=await settle(page)
        check(data['state']['objects']==objects+4,'Control+Y on the model canvas restores the operation')
        await click('CommandInput'); await page.keyboard.type('unsubmitted draft',delay=15)
        await page.keyboard.press('Control+z'); data=await settle(page)
        check(data['state']['objects']==objects+4,'A native text editor retains its own undo instead of undoing the document')
        await page.keyboard.press('Escape'); await settle(page)
        for _ in range(4): await command('Undo')
        check((await state())['objects']==objects,'Model edits remain separate undo records after shell commands and text editing')

        # Navigation leaves the document untouched and Escape restores all five
        # camera quantities, not only the currently changed axis.
        await command('SelNone'); await click('Viewport_Perspective')
        doc_revision=(await state())['documentRevision']
        before=await camera(); _,data,_=await gesture('Perspective')
        after=data['state']['cameras']['Perspective']
        check(after['azimuth']!=before['azimuth'] and after['target']==before['target'],
              'Right drag orbits Perspective without moving its target')
        before=after; _,data,_=await gesture('Perspective',modifier='Shift')
        after=data['state']['cameras']['Perspective']
        check(after['target']!=before['target'] and after['azimuth']==before['azimuth'],
              'Shift+right drag pans Perspective without changing orbit angles')
        before=after; _,data,v=await gesture('Perspective',modifier='Control')
        after=data['state']['cameras']['Perspective']
        anchor=projected_anchor(before,after,.42,.43,v['width'],v['height'])
        check(after['distance']!=before['distance'] and abs(anchor[0]-.42*v['width'])<.01 and abs(anchor[1]-.43*v['height'])<.01,
              'Control+right drag zooms around the actual press point on the target plane')
        before=after; _,data,_=await gesture('Perspective',button='middle')
        after=data['state']['cameras']['Perspective']
        check(after['target']!=before['target'] and after['azimuth']==before['azimuth'],
              'Middle-button dragging pans the model view')
        before=await camera('Top'); _,data,_=await gesture('Top')
        after=data['state']['cameras']['Top']
        check(after['target']!=before['target'] and after['azimuth']==before['azimuth'],
              'Right drag pans a parallel viewport instead of attempting to orbit it')
        before=await camera(); during,data,_=await gesture('Perspective',cancel=True)
        check(during['state']['cameras']['Perspective']!=before and data['state']['cameras']['Perspective']==before,
              'Escape restores the complete camera before a navigation gesture')
        before=await camera(); v=await view('Perspective')
        await page.mouse.move(v['x']+.3*v['width'],v['y']+.62*v['height']); await page.mouse.wheel(0,240)
        data=await settle(page); after=data['state']['cameras']['Perspective']
        anchor=projected_anchor(before,after,.3,.62,v['width'],v['height'])
        # Uno's native browser pointer contract quantizes the synthetic fractional
        # request to an integer DIP. The exact ZoomAt contract is covered by the
        # presentation suite; the end-to-end test therefore requires one-DIP
        # native precision rather than an impossible subpixel reconstruction.
        check(after['distance']>before['distance'] and abs(anchor[0]-.3*v['width'])<=1 and abs(anchor[1]-.62*v['height'])<=1,
              'Wheel zoom moves outward and preserves the cursor target-plane anchor within native DIP precision')
        check(data['state']['documentRevision']==doc_revision and data['state']['objects']==objects,
              'All model navigation modes preserve geometry and document history')

        await command('SelAll'); selected=(await state())['selected']
        await click('Viewport_Front'); data=await settle(page)
        check(selected>0 and data['state']['selected']==selected and data['state']['activeViewport']=='Front',
              'Clicking a viewport title changes the active view without clearing selection')
        await page.wait_for_timeout(650); await click('Viewport_Front',double=True); data=await settle(page)
        check(data['state']['maximized']=='Front','Double-clicking the native viewport title maximizes it')
        await command('4View'); await command('SelNone')
        v=await view('Top'); await page.mouse.click(v['x']+.9*v['width'],v['y']+.2*v['height'],click_count=2,delay=75)
        data=await settle(page)
        check(data['state']['maximized'] is None,'Double-clicking empty model space does not unexpectedly maximize the viewport')
        await page.keyboard.press('Control+F1'); data=await settle(page)
        check(data['state']['activeViewport']=='Top' and data['state']['maximized']=='Top',
              'Control+F1 selects and maximizes Top when the model canvas owns focus')
        await page.keyboard.press('Control+m'); data=await settle(page)
        check(data['state']['maximized'] is None,'Control+M restores the four-view layout from the model canvas')

        # Secondary releases must never terminate an initiating-button gesture.
        v=await view('Perspective'); p=(v['x']+.4*v['width'],v['y']+.4*v['height'])
        await page.mouse.move(*p); await page.mouse.down(button='right')
        await page.mouse.move(p[0]+20,p[1]+10,steps=4); before=await camera()
        await page.mouse.down(button='left'); await page.mouse.up(button='left')
        await page.mouse.move(p[0]+55,p[1]+28,steps=4); await settle(page); after=await camera()
        await page.mouse.up(button='right'); data=await settle(page)
        check(before!=after and data['state']['cameras']['Perspective']==after and data['state']['objects']==objects,
              'Releasing a secondary button preserves right-button navigation ownership and does not repeat a command')

        # View-only layout persistence, keyboard accessibility and cancellation.
        before=(await state())['inspectorWidth']; box=(await read_ui(page))['controls']['InspectorSplitter']
        p=(box['x']+box['width']/2,box['y']+box['height']/2)
        await page.mouse.move(*p); await page.mouse.down(); await page.mouse.move(p[0]-70,p[1],steps=8); await page.mouse.up()
        data=await settle(page); width=data['state']['inspectorWidth']
        check(width>before+60,'Dragging the actual inspector splitter commits a wider properties panel')
        await page.keyboard.press('ArrowRight'); data=await settle(page)
        check(abs(data['state']['inspectorWidth']-(width-16))<1e-8,'Focused inspector splitter supports keyboard resizing')
        width=data['state']['inspectorWidth']; box=data['controls']['InspectorSplitter']; p=(box['x']+box['width']/2,box['y']+box['height']/2)
        await page.mouse.move(*p); await page.mouse.down(); await page.mouse.move(p[0]-50,p[1],steps=6)
        during=await settle(page); await page.keyboard.press('Escape'); await page.mouse.up(); data=await settle(page)
        check(during['state']['inspectorWidth']>width+40 and data['state']['inspectorWidth']==width,
              'Escape restores the inspector width before an uncommitted resize')
        await click('ToggleSidebar'); data=await settle(page)
        check(not data['state']['sidebarVisible'] and 'Side_Line' not in data['controls'],
              'Left sidebar visibility changes actual native chrome, not just a setting')
        await click('ToggleSidebar'); await click('ToggleInspector'); data=await settle(page)
        check(not data['state']['inspectorVisible'] and 'InspectorSplitter' not in data['controls'],
              'Right sidebar toggle removes both inspector and splitter from the layout')
        await page.keyboard.press('F3'); data=await settle(page)
        check(data['state']['inspectorVisible'] and data['state']['inspector']=='Properties',
              'F3 reopens Properties after the right sidebar has been hidden')
        await page.set_viewport_size({'width':680,'height':1050}); data=await settle(page)
        check(data['state']['inspectorVisible'] and 'InspectorSplitter' not in data['controls'],
              'Narrow windows temporarily collapse the inspector without overwriting user preferences')
        await page.set_viewport_size({'width':1600,'height':1100}); data=await settle(page)
        check(data['state']['inspectorWidth']==width and 'InspectorSplitter' in data['controls'],
              'Returning to a wide window restores the user-sized inspector')
        await click('PanelLayers'); await command('Grasshopper'); data=await settle(page)
        check(data['state']['definitionVisible'] and data['state']['inspector']=='Layers',
              'Opening the definition preserves the selected inspector tab')
        await click('GraphDockBottom'); data=await settle(page)
        check(data['state']['inspectorWidth']==width and data['controls']['GraphCanvas']['y']>data['controls']['CadCanvas']['y'],
              'Definition docking no longer resets the user-sized inspector')
        await command('Grasshopper'); await command('Properties'); await command('SelNone'); await click('Viewport_Perspective')

        # The inspector commits complete validated camera drafts atomically.
        before=await camera(); await editor('ViewportPropertyX','12.5'); data=await settle(page)
        check(data['state']['viewportPropertyDraft'] and data['state']['cameras']['Perspective']==before,
              'Editing camera text creates a draft without changing the live camera')
        await editor('ViewportPropertyDistance','NaN'); await click('ApplyViewportProperties'); data=await settle(page)
        check(data['state']['viewportPropertyValidation'] and data['state']['cameras']['Perspective']==before,
              'Invalid camera input rejects the entire draft without partially moving the viewport')
        await click('RevertViewportProperties'); data=await settle(page)
        check(not data['state']['viewportPropertyDraft'] and data['controls']['ViewportPropertyX']['text']!= '12.5',
              'Revert restores native camera fields and discards an invalid draft')
        await editor('ViewportPropertyX','12.5'); await editor('ViewportPropertyDistance','240')
        await click('ApplyViewportProperties'); data=await settle(page)
        check(data['state']['cameras']['Perspective']['target'][0]==12.5 and data['state']['cameras']['Perspective']['distance']==240 and not data['state']['viewportPropertyDraft'],
              'Apply commits all validated camera fields together')
        await editor('ViewportPropertyX','99'); await click('Viewport_Top'); data=await settle(page)
        check(not data['state']['viewportPropertyDraft'] and data['state']['cameras']['Perspective']['target'][0]==12.5,
              'Changing the active viewport cannot apply a stale draft to the previous camera')
        check(data['controls']['ViewportPropertyScale']['enabled'] and not data['controls']['ViewportPropertyAzimuth']['enabled'],
              'Parallel viewport properties enable view height and disable perspective-only orbit fields')
        await page.screenshot(path=str(ROOT/'evidence/uno-rhino-viewport-properties.png'))

        # Layer status uses the selected objects when present, otherwise current
        # layer. Both operations use the real document transaction history.
        await command('Layers'); await click('NewLayer'); data=await settle(page)
        layer=data['state']['layerRows'][-1]['id']; old_layer=data['state']['activeLayer']
        await click('StatusLayer'); await click('StatusLayer_'+layer); data=await settle(page)
        check(data['state']['activeLayer']==layer,'Status layer menu changes the current layer when no objects are selected')
        await command('Undo'); data=await settle(page)
        check(data['state']['activeLayer']==old_layer,'Changing the current layer through the status bar remains undoable')
        await command('SelAll'); await click('StatusLayer'); await click('StatusLayer_'+layer); data=await settle(page)
        check(data['state']['selectedLayers']==[layer] and data['state']['activeLayer']==old_layer,
              'Status layer menu assigns selected objects without changing the current layer')
        await command('Undo'); await command('SelNone'); await command('Undo')
        check((await state())['layers']==initial['layers'],'Layer assignment and layer creation undo independently')

        # A long right click and keyboard context request expose real native menu
        # actions; the short right click path above remained repeat-only.
        v=await view('Perspective'); p=(v['x']+.7*v['width'],v['y']+.2*v['height'])
        await page.mouse.click(*p,button='right',delay=650); data=await settle(page)
        check(data['state']['modelContextMenuOpen'] and 'ModelContext_Properties' in data['controls'],
              'Holding the right mouse button opens the native model context menu')
        await click('ModelContext_Properties'); data=await settle(page)
        check(data['state']['inspector']=='Properties' and not data['state']['modelContextMenuOpen'],
              'Native model context-menu actions invoke the same workspace commands')
        v=await view('Perspective'); await page.mouse.click(v['x']+.9*v['width'],v['y']+.15*v['height'])
        await page.keyboard.press('Shift+F10'); data=await settle(page)
        check(data['state']['modelContextMenuOpen'],'Shift+F10 opens the model context menu without a mouse hold')
        await page.keyboard.press('Escape'); await settle(page)
        await command('Layers')
        expected=(await state())['preferences']
        # Read-only startup evidence is captured before the fixture changes layout.
        await page.reload(wait_until='domcontentloaded')
        await page.wait_for_function('globalThis.nurbsSceneQualification && globalThis.nurbsSceneQualification.status !== "running"',timeout=180000)
        report=await page.evaluate('globalThis.nurbsSceneQualification')
        reloaded=await wait_ready(page,timeout=20000)
        check(report['status']=='passed' and reloaded['state']['startupPreferences']==expected,
              'A fresh application load restores the exact persisted version-two workspace preferences')
        data=await command('ModelingLayout')
        check(data['state']['gpuError'] is None and data['state']['graphErrors']==0,
              'The real GPU and definition remain error-free after shell, input, camera and persistence workflows')
        await page.screenshot(path=str(ROOT/'evidence/uno-rhino-final.png'))
        (ROOT/'evidence/uno-rhino-workspace.json').write_text(json.dumps({'status':'passed','checks':checks,'final':data['state']},indent=2))
    except Exception as error:
        observed=await page.evaluate('globalThis.nurbsSceneUi')
        (ROOT/'evidence/uno-rhino-workspace.json').write_text(json.dumps({'status':'failed','error':str(error),'checks':checks,'observed':observed},indent=2))
        raise
    return checks
