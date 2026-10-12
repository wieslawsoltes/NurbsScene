# Panel display options and Unicode preview review

## Independent, persistent display controls

Text and Panel context menus expose **Draw paths**, **Draw indices** and
**Wrap items**. These edit optional immutable `PanelDisplayOptions`, not component
parameters or the text data tree. Panel's historical defaults are paths on,
indices off and wrapping on; Text defaults to paths off. Returning to defaults
removes redundant metadata. These are independently implemented conventions, not
binary, file-format or pixel-identical Grasshopper compatibility.

One menu action has one undo record and rejects a stale/deleted/replaced owner.
The same solved node results and native wire paths remain reusable. Indices restart
at zero for each branch and identify items rather than wrapped display rows.
Disabling wrapping retains explicit newlines and marks clipped rows with an
ellipsis. Empty connected trees still override stored text.

Optional options survive exact `.nscene` workspaces, graph fragments, duplicate,
copy/paste and undo/redo without changing the workspace version. Old files retain
legacy defaults. The original PR11 history is merged with main through PR12,
preserving all-edge panel resizing, low-zoom readability, fresh native search
sessions, ellipse/loft and stable document ordering.

## Bounded text traversal and correctness

`PanelTextRows` traverses spans with .NET's extended-grapheme API rather than
allocating a substring for every character. A bounded stack buffer joins an item
index to its data before segmentation, preserving the boundary for a leading
combining mark. One builder is reused across output rows. Nonwrapping invisible
tails use a bounded CR/LF search instead of segmenting every clipped character.

Limits remain 64 displayed rows, 128 text elements per row, 8192 input UTF-16 units
per preview and 2048 input units per item. Index prefixes use an additional bounded
buffer. Branch visits are capped even when hidden empty branches emit no rows.
These are text-formatting limits, not pixel-measured wrapping or a scrolling view.

A UTF-16 prefix is not necessarily a grapheme boundary. At a truncated scan window,
the final cluster is conservatively omitted and replaced with an ellipsis: it may
continue as combining marks, a regional-indicator pair, a joined emoji or CRLF.
This can omit one otherwise complete final character at the scan limit, rather
than risk displaying a partial cluster or scanning an unbounded tail. Full input
below the limit is segmented normally. Complete clusters over 64 UTF-16 units and
malformed surrogate sequences use a visible replacement and flag truncation.
The original in-memory text is never rewritten by preview formatting.

Reference for the segmentation primitive:
https://learn.microsoft.com/en-us/dotnet/api/system.globalization.stringinfo.getnexttextelementlength

This does not implement a font fallback/shaping engine, all bidi/script behavior,
font/alignment settings, scrolling every panel item or exact Grasshopper display.

## Regression and benchmark contracts

The panel-display executable retains its original 18 metadata/history/cache tests
and adds 12 text-review contracts. Seven failed on the original implementation:
three cutoff boundaries, oversized cluster diagnostics, malformed UTF-16 and two
changed-preview allocation bounds. All 30 pass after the change. The independent
small-input oracle runs 1,000 deterministic Unicode cases using full-text allocating
enumeration rather than the production bounded-span traversal. Warm cache reads
remain zero-allocation; old immutable preview results survive subsequent builds.

`tests/panel-display-performance` uses only APIs present in original PR11 head
`4c6bd761431c9a86fef74f8361aa3b8118e75b3f`. The workflow copies the identical harness
to that baseline and compares it with the candidate: Release, tiered compilation
disabled, 20 warmups and seven samples of 500 operations. Changed-preview workloads
alternate column budgets to measure formatting, not only cached reads. Raw timings,
allocation samples, environment and tested revisions are retained in CI artifacts.
These are CPU formatting/cache workloads, not GPU, text rasterization, FPS or
whole-application speed measurements. No timing threshold is a CI pass condition.

## Actual Uno validation

`test_uno.py` retains five previous mandatory campaigns and adds panel-display as
the sixth. Its read helpers use the captured, satisfied observation snapshot.
The panel campaign uses native pointer/keyboard input to resize, toggle options,
edit Unicode text, duplicate, undo and redo. Its observations report the immutable
preview passed to the actual panel draw, not a separately recomputed expected view.
Every campaign also runs the same managed modeling and differential framebuffer
checks. No mock GPU, retry-to-pass, skipped scenario or alternate harness replaces
this qualification. Per-commit CI results determine platform qualification;
managed contract success alone is not a passed Uno application build.

## Slider input qualification correction

The first formatter-head Uno run passed the workspace campaign but failed in the
existing slider campaign before reaching the new panel scenario. Its screenshot
showed Minimum `80` and Maximum `10`: the test intended to enter `80` into Maximum,
but its click at the outer TextBox midpoint hit the header/editor seam and left
Minimum focused. The control bounds include the header; validation correctly
rejected this actual draft. This was not a changed numerical range rule or a panel
formatter failure, and simply rerunning the same input was not used as a fix.

The test now targets the lower editor area, observes that the intended control
really has focus, and verifies its actual captured text after typing. A bounded,
read-only slider draft/focus observation exists only in qualification-enabled
builds. Four coordinate unit contracts cover the observed 53-pixel control and
invalid/small bounds. The original invalid-range, rounded-value, history, wire
reuse and inspector assertions remain, with an additional all-fields draft check.
No programmatic focus, direct graph mutation or altered Apply validation is used.
