"""Fresh observation barrier after real input; no application mutation.

The app publishes snapshots periodically. A delay alone can still return the
pre-click snapshot, especially when native input/layout work occupies a frame.
Require two samples published after the input call before using visual bounds.
Assertions about the actual action remain in their respective regression tests.
"""
async def settle(page):
    sample = await page.evaluate('globalThis.nurbsSceneUi?.sample ?? -1')
    await page.wait_for_function(
        'globalThis.nurbsSceneUi?.status !== "ready" || globalThis.nurbsSceneUi.sample >= '+str(sample+2),
        timeout=20000)
    value = await page.evaluate('globalThis.nurbsSceneUi')
    if value.get('status') != 'ready':
        raise AssertionError(value)
    return value
