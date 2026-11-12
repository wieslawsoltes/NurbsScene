# Bundled WebGPU and XAML fonts

The four DejaVu Sans faces provide deterministic offline text for the WebGPU
graph canvas and a complete regular/bold/oblique fallback family for XAML. The fonts are the unmodified DejaVu 2.37 upstream release, with
their original upstream filenames. They carry the accompanying
[DejaVu license](LICENSE-DejaVu.txt); they are not covered by the application's MIT
license.

Upstream: https://github.com/dejavu-fonts/dejavu-fonts/releases/tag/version_2_37

Retrieval mirror: https://github.com/go-opentype/fonts/tree/main/dejavusans
(the mirror's italic and bolditalic filenames map to Oblique and BoldOblique).

| File | Git blob |
|---|---|
| DejaVuSans.ttf | e5f7eecce43be41ff0703ed99e1553029b849f14 |
| DejaVuSans-Bold.ttf | 6d65fa7dc41ae8ffae77a4a843a73ba31ffd78c7 |
| DejaVuSans-Oblique.ttf | 999bac7714134bc7d931efac92b962a93c52e094 |
| DejaVuSans-BoldOblique.ttf | 753f2d80b1f9a13026d641b6fd4cafb8d85dd479 |

The packaged font names and version strings were checked directly in their sfnt
name tables. The global XAML provider resolves the DejaVu Sans family to these embedded faces on every host.
Other requested desktop families retain Uno's managed installed-font matching.
Missing families use the bundled face with the requested weight and slant. The bundled fonts do not cover every Unicode script; a full font
fallback system for scripts outside their coverage is a separate requirement.

## Host registration and style matching

Both application heads register `BundledFonts.CreateProvider()` before creating
the XAML tree. This public `IFontProvider` wrapper keeps one managed provider for
installed fonts and platform character fallback, and selects the embedded
regular, bold, oblique or bold-oblique face for the packaged family. Semibold
(weight 600) and heavier requests use the real bold outlines; italic and oblique
requests use the real oblique outlines. The bundled family has normal-width,
static 400 and 700 faces, so other weights and stretches use the nearest supplied
face.

Uno 7.0.32's managed provider accepts one bundled default font and caches it by
size. Supplying only the regular bytes makes XAML bold/italic requests resolve to
the regular face on a browser without installed fonts. The wrapper fixes that
observable typography-preview mismatch without synthesizing a style or changing
the custom vector canvas's independent font resolver. Parsed XAML handles are
cached by face and exact pixel size, with a bounded 128-entry resolution cache.
Font bytes are loaded only when the corresponding face is first requested.

The wrapper uses public Uno interfaces and preserves raw application font loading
and asynchronous platform fallback for characters outside the bundled coverage:

- https://github.com/unoplatform/uno/blob/7.0.32/src/Uno.UI.Composition.Drawing/IFontProvider.cs
- https://github.com/unoplatform/uno/blob/7.0.32/src/Uno.UI.Composition.Managed/ManagedFontManager.cs

## Vector font lifetime and continuous zoom

Custom vector text resolves each requested physical family/style once at a
canonical size of 64. Uno's managed installed-font provider retains a font file
for every exact size it resolves; asking it for each `8 / zoom` panel-text size
would retain new font data throughout a continuous zoom gesture, even after the
control's own size cache evicted those entries. Canonical resolution removes
that source of growth. The four embedded fallback faces are also shared at the
canonical size.

Shaping keeps HarfBuzz's actual glyph IDs, kerning, ligatures, advances and
offsets. Measurements and vertical metrics scale by the requested size divided
by 64. Drawing applies the same scale once after positioning the baseline, so
the rendered geometry and measured placement agree. Synthetic bold thickness is
specified in canonical coordinates before that scale; synthetic slant remains
dimensionless. `VectorTextBlob.Size` continues to describe the requested size.
`VectorTypeface.ResolutionCount` reports completed physical-face resolutions for
native qualification and profiling.
