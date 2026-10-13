# Canvas navigation and port-query review

## Behavior

Control plus right-button dragging zooms about the pointer-down anchor; upward
motion zooms in, downward motion zooms out. Horizontal motion alone does not zoom.
Right-button dragging without Control pans. The operation is chosen when capture
starts, so releasing Control does not unexpectedly turn a zoom into panning.
Escape or capture cancellation restores the pre-gesture view. Final release
coordinates remain authoritative and sub-threshold right-click jitter does not
move the view. Navigation does not alter selection, the definition or its history.

The shortcut reference is [David Rutten's original canvas guide](https://www.grasshopper3d.com/forum/topics/what-hotkeys-and-shortcuts-are-available-in-grasshopper).
The vertical sensitivity, feedback and implementation are independently authored;
this is not a claim of exact original animation, wheel-menu or mouse-device parity.

`GraphCanvasView` and `GraphCanvasNavigation` expose reusable, platform-independent
navigation. They calculate a complete finite view before the native canvas adopts
it. Wheel, programmatic and drag zoom share the same anchored transform. Scale is
clamped to the existing 0.15–3.5 range before exponentiation. A no-op at a zoom
bound preserves the exact translation instead of repeatedly rounding it. Native
translations and zoom are single precision; authoritative geometry stays binary64.
Unrepresentable translations are rejected rather than producing an infinite view.

## Nearest-port query

A native layout has two fixed-X port columns and monotonically spaced rows.
`GraphHitTesting.PortAt` first rejects columns beyond the current nearest radius,
then binary-searches the two bracketing rows. Sliders have coincident rows and use
the first row. Binary64 coordinate plateaus at very large translations retain the
first matching row, just like the former ascending exhaustive scan. An exact hit
can return immediately because all remaining equal-distance candidates have lower
priority. Frontmost-node, input-side, ascending-row and circular-tolerance rules
are unchanged for finite layouts. Nonfinite presentation ports no longer create
fabricated hits; invalid query arguments still reject explicitly.

This query still visits nodes. It is not a general spatial index, geometric
intersection acceleration or constant-time hit testing. A compatibility fallback
retains the exhaustive row scan for a custom catalog beyond the integer-offset
range of `PresentedNode.Port`. The native canvas also reuses its existing layout
identity dictionary instead of doing an additional LINQ node scan when drawing a
pending wire. No new index or unbounded retained cache was introduced.

## Tests and measurement

The pointer executable retains the original 16 selection/drag contracts and adds
13 navigation/query contracts. These include 1,000 randomized anchored transforms,
10,000 randomized comparisons with an independent exhaustive port scan, huge
coordinate plateaus, frontmost/side/row ties, exact radial edges, malformed bounds,
clamps, overflow rejection and warm allocation contracts.

The mandatory native pointer campaign retains all prior pointer assertions and adds
actual Ctrl+right-drag, anchoring, Control-release capture, Escape zoom/pan recovery,
wheel clamps and unchanged history/geometry/cache checks. It uses observed bounds
and genuine mouse/keyboard input, not graph mutation or forced focus. All seven
other native campaigns and all differential framebuffer checks remain enabled.

`tests/pointer-performance` is copied unchanged to the pre-review head
`0eba9b93df3c82f1e788a7bceb5e7802f8727383` and the candidate. Both run in Release on
one runner with tiered compilation disabled, 20 warm-ups and seven samples for
each workload. Fixtures contain 1,000 ordinary nodes, 250 nodes with 128 inputs,
and 25 nodes with 1,024 inputs. Raw timings, allocations, checksums and environment
metadata are retained with exact source revision manifests. These are complete
CPU port-query calls on synthetic layouts, not rendering, FPS, actual hardware-GPU
or whole-application speedups. Do not infer results before that revision's CI has
completed.

## Qualification and remaining limits

This source continues the recovered canvas-input work in PR #15. Its original six
local commits and three publication commits remain preserved. The current source
must pass its own managed contracts, desktop/WebAssembly builds and all eight
native campaigns before merge. An earlier head's passes are not current-source
qualification. The local environment has no .NET SDK; the independent C/JavaScript
and offline-browser checks do not substitute for native Uno acceptance.

No new solids, arbitrary trims, SubD, formats/plugins, component catalog, full text
shaping, complete docking or direct Uno WebGPU implementation is claimed. Panel
scrolling is separate work. See [the inventory](PARITY.md) for product boundaries.
