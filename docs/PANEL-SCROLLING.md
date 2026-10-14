# Panel item scrolling and integration review

Panels now expose a muted vertical scrollbar, a proportional grip, track paging,
and an item-range footer. The strip owns unmodified wheel input; the body retains
canvas zoom. A selected panel accepts Home/End/PageUp/PageDown. Escape restores a
captured grip's original offset. Scroll offsets are bounded view state, never
serialized graph parameters or history entries. This is item/branch-record
navigation, not full wrapped-line navigation within one long item.

The scrollbar reference is McNeel's
[GH_VerticalScrollBar API](https://developer.rhino3d.com/api/grasshopper/html/T_Grasshopper_GUI_GH_VerticalScrollBar.htm).
Canvas modifiers follow the
[original shortcut guide](https://www.grasshopper3d.com/forum/topics/what-hotkeys-and-shortcuts-are-available-in-grasshopper).
Artwork, sensitivity and supplemental keyboard navigation are independently
implemented; exact visual or binary compatibility is not claimed.

## Integration and fixes

PR #14's original four commits are retained and reconciled with merged PR #16.
The nine mandatory native campaigns retain both pointer-selection and scrolling;
no previous campaign is overwritten. Secondary mouse buttons cannot replace the
captured scrollbar's owner. Scrollbars take precedence over adjacent port hit
radii. Definition/source changes, replacement, unloading and cancellation discard
stale captures. Canvas disposal is idempotent and disposed canvases cannot reattach
a graph session. These are not claims of a complete touch/pen/reentrant-host matrix.

The previous JavaScript workflow's failure is reproducible without changing its
assertions: the new PanelScrollingContracts project was missing from the solution.
It is now registered, and the original structural checker passes all 37 projects.
Native numerical, JavaScript, isolated package and offline browser tests also pass.

The prior End-key test could accept an already-painted pre-key snapshot. A native
input sequence now acknowledges processed scroll events, independently of expected
offsets/data. The test waits for that acknowledgement, the appropriate capture
state, the closed editor lifetime, the matching solved revision and the completed
paint. A wrong completed window still reaches and fails the original assertion.
The exact asserted snapshot is saved on failure. No forced focus, graph mutation,
fixed-delay substitution, timeout increase or unchanged-head retry is used.

## Rendering and performance

An immutable prefix index retains one integer per branch and binary-searches
record locations, rather than scanning all skipped items. Only the requested
visible records and a bounded tail page are formatted. Source-tree identity reuses
the index across navigation; retained window caches are bounded to 128 entries.
Long individual values retain explicit Unicode/scan truncation limits.

`PanelWindowRenderer.Prepare` obtains the same native font metrics used to render
text before selecting the visible window and scrollbar width. The canvas paints
one final text layout, not a provisional frame followed by corrective overpainting.
`GetFontMetrics` reuses the existing bounded face cache without publishing glyph
entries or a completed-paint stamp. Cold/warm frames, all nine alignments, End,
short-output width recovery and native glyph safety have raster contracts. Warm
preparation has an allocation contract; no measured result is inferred before CI.

The inherited 100,000-branch index-versus-linear lookup harness retains raw samples
and the source revision. It measures CPU record lookup, not total application
throughput, physical GPU performance, FPS or a pre-release renderer comparison.

## Acceptance

The final source must pass its own 23 workflows, desktop/WebAssembly builds,
19 managed scrolling contracts, 22 native typography/raster contracts, 32 observer
contracts, 26 artifact-gate contracts and all nine real-input campaigns. Scrolling
adds 19 native assertions plus session restoration, for a combined 291 input
assertions. Repeated modeling/framebuffer checks are not additional product
features. A failed or missing shard prevents the exact-source aggregate and Pages
qualification. The local environment has no .NET SDK; local JavaScript and Python
checks do not substitute for managed or native Uno CI.

The current scope does not add full shaping/bidi/font fallback, pixel-measured
paragraph wrapping, arbitrary long-item visual-line scrolling, robust solid/B-rep
modeling, arbitrary trims, SubD, complete formats/plugins/catalog or direct Uno
WebGPU. See [the compatibility inventory](PARITY.md).
