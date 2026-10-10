# NurbsScene

Independent, modular NURBS modeling and visual programming for Uno Platform, targeting desktop and WebAssembly.

This repository is being populated from the preserved NurbsScene alpha source and local Git history. The existing implementation includes the C/WebAssembly numerical kernel, six reusable JavaScript packages, eight managed library projects, the Uno application source, and numerical/browser tests.

**The implementation is partial, not a complete Rhino or Grasshopper replacement.** The managed/Uno sources and actual GPU rendering have not yet passed runtime qualification. Exact B-rep solid modeling, full command/component/file/plugin compatibility, and identical UI remain unfinished. The JavaScript browser harness is not the Uno application.

The preserved baseline ends at local commit `232b4a5`, following implementation/test commits `d6f3427`, `9aba08d`, and `275615d`. Detailed source, validation evidence and the parity inventory will accompany the import. No completed source import or GitHub Pages deployment is claimed by this initialization commit.

NurbsScene-specific source is MIT-licensed. Rhino and Grasshopper are behavioral/interface references; proprietary source, branding, icons and binaries are not included.
