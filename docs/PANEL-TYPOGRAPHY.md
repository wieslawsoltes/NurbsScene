# Panel typography, measured alignment and retained native text

Text and Panel offer a draft-only Typography dialog through their context menu
or Ctrl+double-click. Family name, size (6–72 canvas units at 100%), bold, italic,
horizontal alignment and vertical alignment commit together. Reset stays in the
draft; Cancel and unchanged Apply preserve history. Optional `PanelTextStyle`
metadata survives workspace/fragment/duplicate round trips; restoring defaults
removes it. Typography changes do not alter numerical parameters, outputs, node
bounds or wire paths. Stale owners and invalid serialized settings reject before
changing the active graph. Font names cannot contain file/URI syntax; no font is
embedded, downloaded or exported by this feature.

The native body renderer uses the chosen Skia typeface (with explicit synthetic
bold/italic when necessary) to measure and draw the same text. The resolved family
is observable, since unavailable family names may be substituted by the host.
Alignment uses measured advances and ascent/descent rather than character-count
estimates. Large panels retain the eight-pixel readability floor. Body ink is
clipped separately from the heading and footer; overlong rows remain left-anchored
so their beginning is visible. Font size changes the bounded row/column budget.

`PanelTextPlacementCache` retains local-coordinate positions and measured widths.
Changing alignment reuses measurements; translating, selecting or panning reuses
positions too. Failed measurements cannot poison the previous cache. Public layout
validation rejects negative offsets, excessive budgets and invalid metrics.
`PanelTypographyRenderer` retains Skia text blobs, capped at 128 panel entries and
16 typeface entries with least-recently-used eviction. Deletion, graph replacement,
unload and disposal clear the appropriate native resources. Drawing never stores
native resources in the authoritative graph or serializable metadata.

The native dialog preview uses Uno TextBlock, whereas the canvas uses Skia. Font
resolution may differ. Skia's simple text-blob construction does not provide
complex-script shaping, kerning, per-character fallback or complete bidi behavior.
The existing bounded formatter segments complete graphemes, but segmentation is
not shaping. Wrapping still uses a bounded approximate column budget, not a new
pixel-measured paragraph layout. Full font fallback/shaping, panel scrolling,
original application parity and a direct Uno WebGPU backend remain separate work.

## Validation

The core contracts cover style validation, history, exact interchange, immutable
output/path identity, all nine alignments, cache recovery and randomized placement.
The native raster executable links the production renderer source against pinned
SkiaSharp 3.119.2. It compares actual pixels with an immediate-text reference,
checks changes from size/style/alignment, clipping, bounded eviction and disposal.
A same-process CPU benchmark compares retained text blobs with immediate calls
using identical placement; it is not a before/after whole-app or GPU/FPS benchmark.
No font binaries are included in artifacts.

`typography.yml` runs these suites. `test_typography_ui.py` is the seventh mandatory
actual Uno mouse/keyboard campaign. It checks dialog validation, atomic Apply,
Cancel/no-op/Reset, measured alignment, native menu cache reuse, exact duplication
and restoration. Six existing campaigns and all differential framebuffer checks
remain mandatory. Compilation or numerical contracts alone do not qualify the UI;
use final-commit workflow artifacts for completed results.

The interrupted native implementation was preserved verbatim in recovery commit
40a4b709. The first recovered Uno build diagnosed an obsolete Windows.UI.Text
FontWeights reference; the dialog now uses Microsoft.UI.Text.FontWeights. This
recovery checkpoint was not represented as qualified or merged.

References:
- https://api.skia.org/classSkFont.html (advance widths and font metrics)
- https://api.skia.org/classSkTextBlob.html (retained runs and simple mapping limits)
- https://discourse.mcneel.com/t/no-grasshopper-panel-text-options-in-rhino-8-sr6/181096
  (panel property editing and alignment entry points as behavioral references)

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
