"""Actual Uno typography controls and the retained renderer used by GraphCanvas.
All edits use native mouse/keyboard input; observations never mutate the model.
"""
import json
from pathlib import Path
from ui_observation import read_ui, wait_for_ui, settle, text_editor_point
ROOT=Path(__file__).resolve().parents[2]

async def verify_typography_ui(page):
    checks=[]
    async def click(identity, lower=False, hover=False):
        d=await wait_for_ui(page,'!!value.controls['+json.dumps(identity)+']');b=d['controls'][identity]
        if not b['enabled']:raise AssertionError('Disabled '+identity)
        at=text_editor_point(b) if lower else (b['x']+b['width']/2,b['y']+b['height']/2)
        if hover:await page.mouse.move(*at)
        else:await page.mouse.click(*at)
        return await settle(page)
    async def text(identity,value):
        await click(identity,lower=True);key=json.dumps(identity)
        await wait_for_ui(page,'value.controls['+key+'].focused')
        await page.keyboard.press('Control+A');await page.keyboard.insert_text(value)
        return await wait_for_ui(page,'value.controls['+key+'].text==='+json.dumps(value))
    async def choose(identity,index):
        await click(identity,lower=True);await page.keyboard.press('Home')
        for _ in range(index):await page.keyboard.press('ArrowDown')
        await page.keyboard.press('Enter');return await settle(page)
    def check(ok,name):
        if not ok:raise AssertionError(name)
        checks.append(name);print('TYPOGRAPHY CHECK '+name,flush=True)
        (ROOT/'evidence/uno-typography-progress.json').write_text(json.dumps({'checks':checks},indent=2))
    await click('GraphDockBottom');original=await click('GraphFit');b=original['controls']['GraphCanvas']
    await page.mouse.click(b['x']+2,b['y']+2);await page.keyboard.press('Control+A');await page.keyboard.press('Delete')
    await wait_for_ui(page,'value.state.nodeCount===0')
    await page.mouse.dblclick(b['x']+b['width']/2,b['y']+30,delay=80)
    await wait_for_ui(page,'value.state.searchOpen');await click('CanvasComponentSearch')
    await page.keyboard.insert_text('//WWW iii');await page.keyboard.press('Enter')
    await wait_for_ui(page,'value.state.nodeCount===1 && !value.state.searchOpen')
    data=await click('GraphFit');pid=data['nodes'][0]['id'];key=json.dumps(pid)
    def panel(d):return next(p for p in d['typography']['panels'] if p['id']==pid)
    async def node():return next(n for n in (await read_ui(page))['nodes'] if n['id']==pid)
    async def open_dialog():
        n=await node();await page.keyboard.down('Control')
        await page.mouse.dblclick(n['x']+n['width']/2,n['y']+8,delay=80);await page.keyboard.up('Control')
        return await wait_for_ui(page,'value.typography.editorOpen')
    async def rendered():
        # A completed solve can precede the next paint after Undo. Equal style and
        # revision numbers do not prove which graph/result the renderer consumed.
        return await wait_for_ui(page,'value.values.solvedRevision===value.state.graphRevision && value.typography.panels.some(p=>p.id==='+key+'&&p.current&&p.rendered!==null&&p.style.size===p.rendered.style.size&&p.style.horizontal===p.rendered.style.horizontal&&p.style.vertical===p.rendered.style.vertical)')
    data=await rendered();check(not panel(data)['hasStyle'],'Legacy panel begins with absent typography metadata')
    # Exercise every native alignment combination, undoing only actual changes
    # between cases. No-op clicks must not invent history or clear defaults.
    for horizontal in ('Left','Center','Right'):
        for vertical in ('Top','Center','Bottom'):
            start=await rendered()
            for option in ('H'+horizontal,'V'+vertical):
                n=await node();await page.mouse.click(n['x']+n['width']/2,n['y']+8,button='right');await settle(page)
                await click('GraphPanel_Alignment',hover=True);await click('GraphPanel_'+option)
            d=await rendered();item=panel(d);drawn=item['rendered'];clip=drawn['clip'];rows=drawn['rows']
            hf=('Left','Center','Right').index(horizontal)/2;vf=('Top','Center','Bottom').index(vertical)/2
            ink_height=rows[-1]['baseline']-rows[0]['baseline']+drawn['descent']-drawn['ascent']
            check(item['style']['horizontal']==horizontal and item['style']['vertical']==vertical and
                  all(abs(r['x']-(clip['X']+max(0,clip['Width']-r['advance'])*hf))<1e-4 for r in rows) and
                  abs(rows[0]['baseline']+drawn['ascent']-(clip['Y']+max(0,clip['Height']-ink_height)*vf))<1e-4,
                  'Native alignment menu places measured text '+horizontal+'/'+vertical)
            check(d['typography']['glyphBuilds']==start['typography']['glyphBuilds'] and
                  d['typography']['measurements']==start['typography']['measurements'],
                  'Alignment '+horizontal+'/'+vertical+' retains native glyphs and measurements: '+json.dumps({
                      'before':{k:start['typography'][k] for k in ('glyphBuilds','measurements','placements')},
                      'after':{k:d['typography'][k] for k in ('glyphBuilds','measurements','placements')}}))
            edits=d['state']['graphRevision']-start['state']['graphRevision']
            for _ in range(edits):await click('GraphUndo')
    data=await rendered()
    revision=data['state']['graphRevision'];await open_dialog();await text('TypographySize','0');await click('TypographyApply')
    data=await wait_for_ui(page,'value.typography.validation.length>0')
    check(data['typography']['editorOpen'] and data['state']['graphRevision']==revision,'Invalid typography remains a draft without graph mutation')
    await text('TypographySize','14');await text('TypographyFamily','DejaVu Sans')
    await click('TypographyBold');await click('TypographyItalic');await choose('TypographyHorizontal',2);await choose('TypographyVertical',2)
    await page.screenshot(path=str(ROOT/'evidence/uno-typography-dialog.png'))
    await click('TypographyApply');await wait_for_ui(page,'!value.typography.editorOpen');data=await rendered();p=panel(data)
    check(p['style']=={'fontFamily':'DejaVu Sans','size':14,'bold':True,'italic':True,'horizontal':'Right','vertical':'Bottom'},'Apply commits font family size weight slant and both alignments together')
    check(data['state']['graphRevision']==revision+1 and data['values']['evaluated']==0,'Typography commits one revision while retaining solved data')
    placed=p['rendered'];clip=placed['clip'];rows=placed['rows']
    check(bool(placed['family']) and placed['style']==p['style'] and len(rows)>0,'Actual Skia renderer reports the requested style and resolved family')
    check(all(abs(r['x']+r['advance']-(clip['X']+clip['Width']))<1e-4 for r in rows),'Right alignment uses measured native text advances')
    check(abs(rows[-1]['baseline']+placed['descent']-clip['Y']-clip['Height'])<1e-4,'Bottom alignment uses actual native ascent and descent')
    check(next(p for p in data['values']['panels'] if p['id']==pid)['items']==['WWW iii'],'Font and alignment never rewrite panel data')
    await page.screenshot(path=str(ROOT/'evidence/uno-typography-panel.png'))
    before=data['typography'];n=await node();await page.mouse.click(n['x']+n['width']/2,n['y']+8);data=await settle(page)
    check(data['typography']['glyphBuilds']==before['glyphBuilds'] and data['typography']['measurements']==before['measurements'],'Selecting a panel reuses native glyph runs and measured widths')
    await page.mouse.click(n['x']+n['width']/2,n['y']+8,button='right');await settle(page)
    await click('GraphPanel_Alignment',hover=True);await click('GraphPanel_HCenter');data=await rendered()
    check(panel(data)['style']['horizontal']=='Center' and data['typography']['glyphBuilds']==before['glyphBuilds'] and data['typography']['measurements']==before['measurements'],'Native alignment submenu changes positions without recreating glyphs or measurements')
    await click('GraphUndo');data=await rendered();check(panel(data)['style']['horizontal']=='Right' and data['state']['graphRedo'],'Alignment edit undoes independently')
    revision=data['state']['graphRevision'];await open_dialog();await click('TypographyApply');await wait_for_ui(page,'!value.typography.editorOpen');data=await settle(page)
    check(data['state']['graphRevision']==revision and data['state']['graphRedo'],'Unchanged typography Apply preserves redo and revision')
    await open_dialog();await click('TypographyReset');await click('TypographyCancel');await wait_for_ui(page,'!value.typography.editorOpen');data=await settle(page)
    check(panel(data)['style']['size']==14 and data['state']['graphRevision']==revision and data['state']['graphRedo'],'Reset then Cancel discards the draft and preserves history')
    # Copy/duplicate uses the normal graph shortcut rather than a direct API.
    n=await node();await page.mouse.click(n['x']+n['width']/2,n['y']+8);await page.keyboard.press('Control+D');data=await settle(page)
    check(data['state']['nodeCount']==2 and all(p['style']==panel(data)['style'] for p in data['typography']['panels']),'Duplicate preserves complete typography under new node identities')
    await click('GraphUndo');await click('GraphUndo');data=await rendered()
    check(not panel(data)['hasStyle'],'One undo restores absent default style metadata')
    await click('GraphUndo');await click('GraphUndo');await click('GraphDockRight');final=await click('GraphFit')
    check({n['id'] for n in final['nodes']}=={n['id'] for n in original['nodes']} and final['state']['wireCount']==original['state']['wireCount'] and final['state']['graphErrors']==0 and final['state']['gpuError'] is None,'Typography campaign restores the original workspace without graph or renderer errors')
    (ROOT/'evidence/uno-typography-ui.json').write_text(json.dumps({'status':'passed','checks':checks,'final':final['state']},indent=2))
    return checks
