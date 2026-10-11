# Data-tree and path contract

`DataPath` is immutable and compares indices numerically and lexicographically: `{2;2}` sorts before `{2;10}`. Paths contain up to 64 nonnegative Int32 indices. The parser accepts `{0;2;10}`, `0;2;10` and the empty path `{}`; malformed braces, fractional/negative/overflowing indices and empty segments are rejected before an editor transaction changes the graph.

`DataTree` retains sparse paths, explicit null items and empty branches. Construction merges duplicate paths in input order, with at most 100,000 branches and 1,000,000 items. Persistent input trees are used as trees, rather than wrapped as a single item. Typed ports validate items; an `Any` port can carry null. Geometry and parameter values must obey the immutable cache contract.

## Eight additional components

| Component | Behavior |
|---|---|
| `ConstructPath` | Builds a typed path from integer indices |
| `DeconstructPath` | Produces the indices of a typed path |
| `TreePaths` | Enumerates paths and item counts, including empty branches |
| `TreeBranch` | Retrieves a branch by typed path and reports whether it exists |
| `ReplacePath` | Renames a branch; collisions merge items stably |
| `TrimTreePaths` | Removes trailing indices and merges resulting collisions |
| `CleanTree` | Independently removes null items and/or empty branches |
| `FlattenToPath` | Flattens items under an explicitly supplied path |

Paths serialize as tagged values in the managed `.nscene` format, including when nested in parameters. Existing workspaces remain readable. Readers predating typed paths will reject that new value rather than treating it as an arbitrary object.

## Matching and incremental solving

Item ports support longest, shortest and cross-product list matching with bounded output. A single branch can broadcast to structural paths supplied by multibranch inputs; scalar defaults do not introduce phantom root output branches. Tree-access ports receive the entire tree. This is the explicitly tested NurbsScene contract, **not a claim to implement every Grasshopper matching/conversion rule**.

The graph indexes outgoing dependencies and incoming ports. Parallel wires between a pair of nodes retain a dependency reference count, so removing one wire does not incorrectly remove the dependency. Topological solving and downstream invalidation use these indexes. Graph snapshots capture nodes and wires under one lock; cancellation and revision checks prevent stale results from replacing the current solution.

Document selection notifications are suppressed during transactions and emitted with the committed or restored snapshot, preventing observers from seeing partially applied modeling edits.
