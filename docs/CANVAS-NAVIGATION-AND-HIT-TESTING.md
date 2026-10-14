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


## Parallel acceptance without a weaker release gate

The original serial Uno run 38461270948 saved all 271 passing native UI assertions,
all eight completed campaigns and zero page errors, but GitHub concluded the job
as cancelled at its 30-minute envelope. The downloaded evidence archive SHA256 is
`bf50b1fd8714819dd85ba3549439dafbd2b724dbc946ef1734e4c5f85ea2c727`.
Those assertion results are retained; the cancelled workflow is not called green
and its artifact is not used to bypass main/Pages qualification.

The workflow now compiles the browser application once and runs the eight existing
independent campaigns as parallel jobs. The campaign registry is shared by the
runner, CI matrix and aggregate gate. Omitting `--campaign` still runs the complete
local campaign. No UI assertion, per-action deadline, modeling/framebuffer check,
restoration check or browser input operation was removed or loosened. Every shard
retains the original observer contracts. Matrix fail-fast is disabled to preserve
independent failure evidence; a failed shard still prevents qualification.

The build records exact commit/tree identities and SHA256 for every site file,
including `.nojekyll`. Each shard verifies all downloaded bytes and attaches the
manifest it actually checked. The final gate requires successful desktop/build
and all matrix jobs, then independently validates one report for every registered
campaign on the identical publication, minimum UI/modeling assertion counts,
restored sessions, native framebuffer data and zero page/GPU/graph errors.
Missing, duplicate, cancelled, inconsistent or mismatched-source evidence rejects.
Only this gate can upload the `qualified-uno-browser` artifact. The existing Pages
workflow remains unchanged and still requires successful current-main acceptance.

Twenty-six pure artifact-gate contracts exercise these acceptance rules using
synthetic files. They are not browser, rendering or actual Uno tests. Build and
individual campaign jobs each retain a finite 20-minute job budget instead of
extending the old serial deadline. The final artifact gate has a 10-minute budget.
This parallelization trades additional runner use and artifact transfers for lower
wall-clock latency; no fixed CI speedup is promised before a completed run.


## Panel result observation review

The first parallel run (38468174050, source ccccaaa) passed seven campaigns,
including all new pointer/navigation checks, but the retained panels campaign
failed its original multiline single-item assertion. The downloaded failure
archive SHA256 is `ffa4ac27222142b106bb7cdf2a665e83f806f333f1ca1e7f26e9f4f6417bff0b`.
Its later snapshot already has single-item mode, the exact multiline item and a
current solved revision. That later state does not make the failed assertion pass;
the previous harness had not retained the exact snapshot it asserted against.

Code review exposes two independent completion boundaries: ContentDialog unloads
before its ShowAsync continuation applies the graph edit, then the controller
schedules numerical evaluation separately. A wait for editorOpen=false is not
proof of either completion. The read-only qualification observer now includes
editorPending from the existing dialog object's lifetime. It changes no input,
dialog, graph, solver or renderer behavior. Panel output reads require the dialog
lifetime to have ended and the solved revision to match the current graph. Apply
additionally requires a revision transition from the pre-Apply snapshot. No wait
predicate includes an expected output value; a completed wrong value still fails
the original assertion immediately. The exact asserted panel snapshot is retained
on failure rather than replaced by a later observation.

Six additional observer contracts reproduce the unloaded-but-uncommitted and
committed-but-unsolved states, retain wrong completed results, validate transition
constraints, preserve the original deadline and propagate failures without retry.
All previous panel assertions, interaction actions, remaining campaigns and the
strict aggregate gate are unchanged. Current-head acceptance must run again;
there is no retry of the failed unchanged source or relaxed timeout.
