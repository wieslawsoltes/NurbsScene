# Panel typography, measured alignment and retained WebGPU text

Text and Panel offer a draft-only Typography dialog through their context menu
or Ctrl+double-click. Family name, size (6–72 canvas units at 100%), bold, italic,
horizontal alignment and vertical alignment commit together. Reset stays in the
draft; Cancel and unchanged Apply preserve history. Optional `PanelTextStyle`
metadata survives workspace/fragment/duplicate round trips; restoring defaults
removes it. Typography changes do not alter numerical parameters, outputs, node
bounds or wire paths. Stale owners and invalid serialized settings reject before
changing the active graph. Font names cannot contain file/URI syntax.

## Uno 7 drawing and font resources

The application body renderer uses Uno's managed font and shaping interfaces,
retains shaped glyph runs, and draws through the WebGPU backend. The chosen or
substituted font measures and draws the same text. The resolved family is
observable because unavailable names may be substituted. Alignment uses measured
advances and ascent/descent rather than character counts. Large panels retain the
eight-pixel readability floor. Body ink is clipped separately from the heading and
footer; overlong rows remain left-anchored so their beginning is visible. Font size
changes the bounded row/column budget.

The application embeds DejaVu Sans regular, bold, oblique and bold-oblique fonts
for offline fallback. Their [license](../src/NurbsScene.Controls/Drawing/Fonts/LICENSE-DejaVu.txt)
is included with the library and copied to output/publication. A document stores
only style metadata; it neither contains font binaries nor downloads a selected
family. Host font availability can still affect appearance.

`PanelTextPlacementCache` retains local-coordinate positions and measured widths.
Changing alignment reuses measurements; translating, selecting or panning reuses
positions too. Failed measurements cannot poison the previous cache. Public layout
validation rejects negative offsets, excessive budgets and invalid metrics.
`PanelTypographyRenderer` retains glyph resources, capped at 128 panel entries and
16 typeface entries with least-recently-used eviction. Deletion, graph replacement,
unload and disposal clear the appropriate resources. Drawing never stores GPU or
font resources in the authoritative graph or serializable metadata.

The dialog preview uses Uno TextBlock and the canvas uses the custom drawing
adapter; their font resolution and caches can differ. Shaping individual runs does
not establish full mixed-direction paragraph layout, per-character fallback or
every script's visual quality. The bounded formatter segments complete graphemes,
and wrapping still uses an approximate column budget rather than pixel-measured
paragraph layout. [Panel item scrolling](PANEL-SCROLLING.md) remains available with
its documented limits. The [migration journal](UNO7-WEBGPU-MIGRATION.md) documents
the shared-device WebGPU adapter and rendering qualification.

## Validation

The core contracts cover style validation, history, exact interchange, immutable
output/path identity, all nine alignments, cache recovery and randomized placement.
The standalone raster executable defines `NURBSSCENE_SKIA_RASTER_TEST` and retains
the pinned SkiaSharp 3.119.2 branch as a CPU reference for existing layout/cache
contracts. It compares pixels with an immediate-text reference and checks
size/style/alignment, clipping, bounded eviction and disposal. These CPU reference
pixels and timings do not qualify production WebGPU rendering.

`typography.yml` continues to run those suites. `test_typography_ui.py` remains a
mandatory actual Uno mouse/keyboard campaign. It checks dialog validation, atomic
Apply, Cancel/no-op/Reset, measured alignment, menu cache reuse, exact duplication
and restoration. All ten registered campaigns and differential framebuffer checks
remain mandatory; the production WGSL pipeline job adds shader/API coverage.
Compilation or numerical contracts alone do not qualify the UI. Consult the
exact commit/tree workflow artifacts for results on the reconstructed source.

## Historical implementation evidence

The account below records the earlier Skia renderer and its original qualification
revision. Its shaping restrictions and CPU measurements are historical evidence;
the current Uno 7/WebGPU path is described above. Recovery commit `40a4b709`
preserved that interrupted implementation, and its first recovered build corrected
an obsolete `Windows.UI.Text.FontWeights` reference to
`Microsoft.UI.Text.FontWeights`.

## Solve/paint handoff and equal-output cache review

Run 38383104816 passed the six existing input campaigns but failed the typography
campaign's cache assertion at Left/Bottom following Undo. Its waiter required a
completed solve and equal style properties, but those conditions do not establish
that the renderer consumed that solve: history reconstructs the graph and numerical
revision values may be reused. A waiting/previous frame can look identical.

A successful native draw now publishes a `PanelPaintStamp` of its exact owner,
node and result identities, zoom and bounds. Qualification observes this stamp;
it waits for the current solved inputs to have actually been drawn before taking
cache-counter baselines. Reading the stamp does not draw, solve, force focus or
change the document. The original alignment and cache-count assertions remain.
Progress and before/after counters are saved even when a campaign fails. Local
browser reproduction was attempted but navigation was blocked by the environment's
administrator policy; that policy was not modified.

Separately, re-solving can create a new immutable row array with identical text.
Both measured placement and native glyph caches now compare content only when
array identity changes, then adopt the new identity. Equal rows reuse widths,
positions and glyphs; equal-length different text, font identity or size changes
still invalidate the appropriate cache. Four managed regressions fail on the prior
cache and pass with this change. Additional native raster contracts check equal
pixels, source publication and invalidation. Temporary native font-style requests
are deterministically disposed rather than left for finalization.

These changes do not claim a successful Uno run until its final-head evidence
passes, nor do they alter the renderer's documented shaping/fallback limitations.
