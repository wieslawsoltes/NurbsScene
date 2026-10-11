# @nurbsscene/io

Reusable NurbsScene io module.

Version `0.1.0-alpha.1`. Independent source implementation, not full Rhino/Grasshopper compatibility. Runtime dependencies are declared explicitly as scoped NurbsScene packages. Public module exports are listed in `package.json`.

Custom exact workspace JSON (`nurbsscene.workspace`, v1), display OBJ/STL exports and browser download helpers. No `.3dm`, `.gh`, `.ghx` or managed-workspace compatibility. Display meshes can be open; export is not a watertight-solid guarantee.

See the source repository archive `README.md`, `docs/ARCHITECTURE.md`, `docs/PARITY.md` and `docs/VALIDATION.md` for detailed boundaries. MIT license applies to this package's own source. No npm registry publication has occurred.
