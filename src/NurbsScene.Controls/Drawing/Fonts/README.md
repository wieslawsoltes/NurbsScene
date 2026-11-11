# Bundled vector fonts

The four DejaVu Sans faces provide deterministic offline text for the WebGPU
graph canvas. The fonts are the unmodified DejaVu 2.37 upstream release, with
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
name tables. Installed desktop families can override these defaults when the user
requests them. Missing families use the bundled face with the requested weight
and slant. The bundled fonts do not cover every Unicode script; a full font
fallback system for scripts outside their coverage is a separate requirement.
