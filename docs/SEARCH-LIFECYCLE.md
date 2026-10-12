# Cursor search lifecycle regression

PR #6's failed browser run `38180157779` showed a visible, focused search popup in
its screenshot while the reported `searchOpen` flag remained false. The screenshot
alone did not qualify the final edge placement, insertion, reopening or dismissal.

The pinned Uno 6.7.135 `FlyoutBase.ShowAtCore` sets `IsOpen` when it opens the popup,
but schedules its public `Opened` event at dispatcher Idle. The editor previously
used only that deferred event to set its shadow open flag and initial focus. That
allows an already visible popup to be reported closed while input/rendering work
keeps the idle callback pending.

The editor now observes `FlyoutBase.IsOpenProperty` and focuses the loaded search
field through a guarded dispatcher callback. It does not depend on `Opened` for
state, inject input or bypass any browser assertion. A late Closed notification
reads current IsOpen and cannot clear the new popup's suggestions after reopening.
The existing actual-pointer search-open, whole-panel visibility, insertion,
reopening and text-focus tests remain enabled.

Framework reference:
https://github.com/unoplatform/uno/blob/6.7.135/src/Uno.UI/UI/Xaml/Controls/Flyout/FlyoutBase.cs

Per-run source artifacts contain a Git archive, history bundle, exact commit/tree
and SHA256 manifest. They are source provenance, not proof that the independent
numerical or browser workflows passed.

The next CI run also exposed stale test coordinates after Zoom Extents. Its routed
trace showed the test pressing 47 pixels right and 22 pixels below the intended
slider: exactly the previous pan offset. The action had completed but the
observation still held pre-Fit screen bounds. The observer now publishes a
monotonic sample number; input helpers await two newly published samples before
reusing bounds. This is observation synchronization, not a new delay, skip,
action injection, or changed correctness expectation. Numerical coordinates,
selection/slider effects and edge visibility still have their original assertions.
