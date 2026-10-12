"""Readiness and fresh-snapshot barriers; never dispatch application actions.

Modeling qualification and the input observer start in separate asynchronous
continuations. A modeling pass is not proof that the observer exists yet.
"""
READY_EXPRESSION = """(() => {
    const observation = globalThis.nurbsSceneUi;
    return !!observation && ["ready", "failed", "restored"].includes(observation.status);
})()"""

async def wait_ready(page, timeout=30000):
    await page.wait_for_function(READY_EXPRESSION, timeout=timeout)
    value = await page.evaluate('globalThis.nurbsSceneUi')
    if not isinstance(value, dict) or value.get('status') != 'ready':
        raise AssertionError({"message": "Workspace observer did not become ready", "observed": value})
    return value

async def settle(page):
    current = await wait_ready(page)
    sample = current.get('sample')
    if not isinstance(sample, int) or isinstance(sample, bool) or sample < 0:
        raise AssertionError({"message": "Invalid workspace observation sample", "observed": current})
    await page.wait_for_function(
        'globalThis.nurbsSceneUi && (globalThis.nurbsSceneUi.status !== "ready" || globalThis.nurbsSceneUi.sample >= '+str(sample+2)+')',
        timeout=20000)
    value = await page.evaluate('globalThis.nurbsSceneUi')
    if not isinstance(value, dict) or value.get('status') != 'ready':
        raise AssertionError({"message": "Workspace observer stopped during input", "observed": value})
    return value
