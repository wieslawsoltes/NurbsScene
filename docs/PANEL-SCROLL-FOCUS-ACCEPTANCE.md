# Panel scrolling focus acceptance

The final-head Uno run `38542606422` for `cc0abb5` compiled both applications and
passed eight native campaigns, but the scrolling campaign stopped while waiting
for `controls.GraphCanvas.focused`. Downloaded failure artifact `11885156957`
SHA256: `52aa2056c317123ff4db49e80918c2b3c4350afc945e4094315ba1119a984e29`.

The exact saved snapshot reports `state.graphFocus = Pointer` and
`state.focusedElement = NurbsScene.Controls.GraphCanvas`, with no open dialog or
active capture. Its box simultaneously reports `focused = false`. This is a
qualification-observer defect, not evidence that clicking failed to focus the
actual canvas. `SKCanvasElement` derives from `FrameworkElement`, whereas the box
observer previously recognized focus only for `Control` instances.

The observer now captures `FocusManager.GetFocusedElement(view.XamlRoot)` once per
sample and also recognizes direct reference identity with that root-local owner.
Existing Control.FocusState handling remains for templated editors. Neither the
observer nor the test calls Focus(), changes selection, routes artificial native
events, alters the document, or assumes focus from graph state. All diagnostics
within a sample use the same focus-owner read. Ordinary builds exclude this code.

The native scrolling campaign retains every earlier interaction and assertion,
including the original canvas-focused wait. Two further checks cover a focused
text editor with an unfocused canvas and agreement between the canvas box,
native FocusState and actual root-local owner after a real pointer click. The
mandatory registry and aggregate contract now require 22 scrolling checks including
restoration: 293 across all nine campaigns. Deadlines, source-byte verification,
modeling/framebuffer checks, and release gates are unchanged.

A completed current-source run, not a retry of the failed source or a previous
head's evidence, is required before merge. This fix does not expand the existing
item/branch-record scrolling scope into full long-item visual-line navigation.

Primary API references:
- https://platform.uno/docs/articles/controls/SKCanvasElement.html
- https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.focusmanager.getfocusedelement
