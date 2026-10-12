# Stable document order and loft input selection

PR #12's first native browser run created the loft but reported the first new
ellipse as the last object. Deleting the populated pavilion and adding new
objects reused Dictionary entry slots; enumeration no longer followed creation
order. This also reversed select-all profile order and could reorder layer rows.
The existing native input assertions are unchanged: creation, exact section
samples, loft undo/redo and component baking still have to pass.

The Model library now records object and layer identities in explicit ordered
lists alongside their dictionaries. Successful insertion appends; updates retain
position; deletion compacts each list once for the whole batch. Reusing a deleted
identity appends it as a new object. Cached read-only value snapshots preserve
this order, including snapshots previously returned to callers.

Selection membership remains a hash set. `SelectionOrder` is a separate cached,
read-only snapshot of the user's ordered picks. Replacement uses supplied order,
additive selection appends only newly selected identities, and delete/hide/lock
retains surviving pick order. Commands consume SelectionOrder; select-all is fed
stable document order. Undo/redo and exact workspace snapshots carry ordered
arrays without changing the workspace format or treating a set's storage order
as chronology. Failed requests validate before altering the order lists.

Lookup and membership remain constant-time on average. Add appends to lists;
batch deletion compacts in O(N + S + K) expected work for N objects, S selected
objects and K deletions, rather than removing from the list K times. Published
views allocate only after invalidation; warmed object/layer/ordered-selection
reads allocate zero managed bytes in the tests. This adds identity-list storage;
it is a correctness fix, not a claimed overall document-performance speedup.

Thirteen managed regressions cover deletion/recreation, reused identities,
updates, layer order, ordered picks, hidden/locked selection, rollback, undo,
redo, exact interchange, invalid requests, deterministic random edit traces and
a 10,000-object batch case. Eight new regressions reproduced the defect on the
previous implementation before the fix. The original ellipse/loft numerical
and native-evaluator contracts remain enabled in the same test executable.

This ordering contract is not Rhino's automatic geometric sorting of preselected
profiles, interactive profile-picking mode, direction/seam alignment or every
loft style. Those remain separate compatibility work. McNeel documents ordered
profiles and seam handling for Loft; the low-level curve-list API accepts the
caller-supplied order. Microsoft documents dictionary enumeration as undefined.

References:
- https://docs.mcneel.com/rhino/8/help/en-us/commands/loft.htm
- https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.brep/createfromloftrefit
- https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2
