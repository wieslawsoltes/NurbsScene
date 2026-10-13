"""Mandatory native Uno selection and gesture regression campaign.

Uses observed bounds with real mouse/keyboard input only. No graph mutations,
forced focus, skipped assertions or replacement HTML canvas are permitted.
"""
import json
from pathlib import Path
from ui_observation import read_ui, settle, wait_for_ui

ROOT = Path(__file__).resolve().parents[2]


async def verify_pointer_ui(page):
    checks = []
    click_number = 0

    def check(condition, name):
        if not condition:
            raise AssertionError(name)
        checks.append(name)
        print("POINTER CHECK " + name, flush=True)

    async def click_control(identity):
        data = await wait_for_ui(page, '!!globalThis.nurbsSceneUi.controls[' + json.dumps(identity) + ']')
        box = data['controls'][identity]
        if not box['enabled']:
            raise AssertionError('Disabled ' + identity)
        await page.mouse.click(box['x'] + box['width'] / 2, box['y'] + box['height'] / 2)
        return await settle(page)

    async def width_node():
        return next(n for n in (await read_ui(page))['nodes'] if n['name'] == 'Width')

    def header(node, fraction=.5):
        # Use a scale-relative label position, never the slider rail or ports.
        return node['x'] + node['width'] * fraction, node['y'] + node['height'] * .18

    async def click_width(*modifiers):
        nonlocal click_number
        node = await width_node()
        # Independent single-click scenarios use separated label positions to
        # avoid accidentally testing the host's double-tap settings dialog.
        click_number += 1
        position = header(node, .25 if click_number % 2 else .75)
        for modifier in modifiers:
            await page.keyboard.down(modifier)
        try:
            await page.mouse.click(*position)
        finally:
            for modifier in reversed(modifiers):
                await page.keyboard.up(modifier)
        return await settle(page)

    async def marquee(*modifiers, reverse=False):
        box = (await read_ui(page))['controls']['GraphCanvas']
        start = (box['x'] + 6, box['y'] + 6)
        end = (box['x'] + box['width'] - 6, box['y'] + box['height'] - 6)
        if reverse:
            start, end = end, start
        for modifier in modifiers:
            await page.keyboard.down(modifier)
        try:
            await page.mouse.move(*start)
            await page.mouse.down()
            await page.mouse.move(*end, steps=8)
            await page.mouse.up()
        finally:
            for modifier in reversed(modifiers):
                await page.keyboard.up(modifier)
        return await settle(page)

    async def clear_selection():
        box = (await read_ui(page))['controls']['GraphCanvas']
        await page.mouse.click(box['x'] + 12, box['y'] + 12)
        data = await settle(page)
        if data['state']['graphSelected']:
            raise AssertionError('Blank canvas did not clear selection')

    def positions(data):
        return {n['id']: (n['modelX'], n['modelY']) for n in data['nodes']}

    def selected(data):
        return set(data['state']['graphSelected'])

    async def original_positions():
        expected = json.dumps({key: list(value) for key, value in initial_positions.items()})
        return await wait_for_ui(page, '(() => {const p=' + expected + ';return globalThis.nurbsSceneUi.nodes.length===' +
            str(len(initial_positions)) + ' && globalThis.nurbsSceneUi.nodes.every(n=>p[n.id] && Math.abs(n.modelX-p[n.id][0])<1e-7 && Math.abs(n.modelY-p[n.id][1])<1e-7)})()')

    await click_control('GraphDockBottom')
    await click_control('GraphFit')
    baseline = await read_ui(page)
    initial_positions = positions(baseline)
    ids = set(initial_positions)
    node = await width_node()
    width_id = node['id']
    await click_width()
    await page.keyboard.press('Control+A')
    data = await settle(page)
    check(selected(data) == ids, 'Ctrl+A selects every component')
    check(selected(await click_width()) == ids, 'Plain click preserves a selected group')
    check(selected(await click_width('Shift')) == ids, 'Shift-click is additive, not a toggle')
    check(selected(await click_width('Control')) == ids - {width_id}, 'Ctrl-click removes only the hit component')
    check(selected(await click_width('Control')) == ids - {width_id}, 'Ctrl-click on an unselected component does not add it')
    check(selected(await click_width('Shift')) == ids, 'Shift-click adds an unselected component')
    check(selected(await click_width('Control', 'Shift')) == ids - {width_id}, 'Ctrl+Shift-click toggles a selected component off')
    data = await click_width('Control', 'Shift')
    check(selected(data) == ids, 'Ctrl+Shift-click toggles an unselected component on')
    check(positions(data) == initial_positions and all(data['state'][key] == baseline['state'][key]
        for key in ('graphRevision', 'graphPathBuilds', 'graphLayoutBuilds', 'meshBuilds')),
        'Selection-only changes preserve geometry, revision, layout, meshes and native wire paths')

    check(not selected(await marquee('Control')), 'Ctrl-marquee removes enclosed components')
    check(selected(await marquee('Shift')) == ids, 'Shift-marquee adds enclosed components')
    check(not selected(await marquee('Control', 'Shift', reverse=True)), 'Ctrl+Shift crossing marquee toggles each intersected component once')
    check(selected(await marquee()) == ids, 'Plain window marquee replaces selection with enclosed components')

    box = (await read_ui(page))['controls']['GraphCanvas']
    await page.mouse.move(box['x'] + 8, box['y'] + 8)
    await page.mouse.down()
    await page.mouse.move(box['x'] + 70, box['y'] + 28, steps=5)
    data = await settle(page)
    check(not selected(data), 'Plain marquee clears its draft selection on pointer-down')
    await page.keyboard.press('Escape')
    await page.mouse.move(box['x'] + 90, box['y'] + 32)
    await page.mouse.up()
    data = await settle(page)
    check(selected(data) == ids and positions(data) == initial_positions, 'Escape restores pre-marquee selection and releases capture')

    await clear_selection()
    node = await width_node()
    x, y = header(node)
    await page.keyboard.down('Shift')
    await page.mouse.move(x, y)
    await page.mouse.down()
    await page.mouse.move(x + 70, y + 12, steps=8)
    await settle(page)
    await page.screenshot(path=str(ROOT / 'evidence/uno-pointer-constrained-guide.png'))
    await page.mouse.up()
    await page.keyboard.up('Shift')
    await settle(page)
    moved = await width_node()
    check(moved['modelX'] > node['modelX'] and abs(moved['modelY'] - node['modelY']) < 1e-7,
        'Shift-drag constrains a near-horizontal move to the horizontal guide')
    await click_control('GraphUndo')
    await original_positions()
    check(True, 'One undo restores the complete constrained drag')
    await click_control('GraphRedo')
    restored = await width_node()
    check(abs(restored['modelX'] - moved['modelX']) < 1e-7 and abs(restored['modelY'] - moved['modelY']) < 1e-7,
        'Redo restores the exact constrained displacement')
    await click_control('GraphUndo')
    await original_positions()

    node = await width_node()
    x, y = header(node)
    await page.mouse.move(x, y)
    await page.mouse.down()
    await page.mouse.move(x + 50, y + 8, steps=5)
    await settle(page)
    free = await width_node()
    await page.keyboard.down('Shift')
    await page.mouse.move(x + 51, y + 8)
    await settle(page)
    constrained = await width_node()
    await page.keyboard.up('Shift')
    await page.mouse.move(x + 52, y + 8)
    await page.mouse.up()
    await settle(page)
    released = await width_node()
    check(free['modelY'] > node['modelY'] and abs(constrained['modelY'] - node['modelY']) < 1e-7
          and released['modelY'] > node['modelY'], 'Shift can be pressed and released during the same drag without coordinate drift')
    await click_control('GraphUndo')
    await original_positions()

    before_cancel = await read_ui(page)
    node = await width_node()
    x, y = header(node)
    await page.mouse.move(x, y)
    await page.mouse.down()
    await page.mouse.move(x + 45, y + 15, steps=5)
    await settle(page)
    check((await width_node())['modelX'] > node['modelX'], 'Uncommitted drag visibly updates the native graph')
    await page.keyboard.press('Escape')
    await page.mouse.move(x + 80, y + 15)
    await page.mouse.up()
    cancelled = await original_positions()
    check(all(cancelled['state'][key] == before_cancel['state'][key] for key in ('graphUndo', 'graphRedo')),
          'Escape cancels drag without consuming history or clearing redo')

    node = await width_node()
    x, y = header(node)
    await page.mouse.move(x, y)
    await page.mouse.down()
    await page.mouse.move(x + 40, y + 12, steps=5)
    await page.mouse.down(button='right')
    await page.mouse.up(button='right')
    await page.mouse.wheel(0, 120)
    await page.mouse.move(x + 65, y + 20, steps=5)
    await page.mouse.up(button='left')
    data = await settle(page)
    moved = await width_node()
    zoom = node['width'] / 180  # Number slider's authored logical width.
    check(abs(moved['modelX'] - node['modelX'] - 65 / zoom) < 1e-5
          and abs(moved['modelY'] - node['modelY'] - 20 / zoom) < 1e-5
          and abs(moved['width'] - node['width']) < 1e-7 and not data['state']['contextMenuOpen'],
          'Secondary-button release and wheel input do not terminate or rescale the owning drag')
    await click_control('GraphUndo')
    await original_positions()

    node = await width_node()
    x, y = header(node)
    await page.mouse.move(x, y)
    await page.mouse.down()
    await page.mouse.move(x + 40, y + 12, steps=5)
    await settle(page)
    await page.keyboard.press('Delete')
    await page.mouse.up()
    data = await wait_for_ui(page, 'globalThis.nurbsSceneUi.state.nodeCount===' + str(len(ids) - 1))
    check(width_id not in {n['id'] for n in data['nodes']}, 'Delete during dragging cancels the draft and removes the selected component')
    await click_control('GraphUndo')
    await original_positions()
    check(True, 'Undo deletion restores pre-drag positions instead of a partial pointer move')

    node = await width_node()
    x, y = header(node)
    await page.mouse.move(x, y)
    await page.mouse.down()
    await page.mouse.move(x + 35, y + 12, steps=5)
    await settle(page)
    await page.keyboard.press('f')
    await page.mouse.up()
    await original_positions()
    check(True, 'Zoom extents cancels an active draft before changing the canvas transform')
    final = await settle(page)
    check(final['state']['graphErrors'] == 0 and final['state']['gpuError'] is None,
          'Selection and gesture campaign leaves the definition and GPU error-free')
    await page.screenshot(path=str(ROOT / 'evidence/uno-pointer-final.png'))
    (ROOT / 'evidence/uno-pointer-ui.json').write_text(json.dumps({'status': 'passed', 'checks': checks,
        'final': final['state']}, indent=2))
    return checks
