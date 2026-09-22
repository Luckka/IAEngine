# Copy Boundaries

```text
SOURCE:
  external OnlineOS repository (path intentionally omitted)

SOURCE POLICY:
  read-only

DISCOVERED_SOURCE_ORCHESTRATOR:
  external OnlineOS orchestrator source (path intentionally omitted)

DESTINATION:
  this repository

DESTINATION POLICY:
  independent evolution

FORBIDDEN:
  writes, commits, branches or configuration changes in source
```

The source was inspected only. No source branch, commit, working-tree file,
configuration, prompt, test, state directory, or generated artifact was changed.

The copied destination baseline intentionally had no `.git`, no historical
`.ai-runs`, and no historical `.ai-state`. This public repository now has its own
Git history; runtime-generated `.ai-runs` or `.ai-state` must still be created below
the destination only. The copied Git lifecycle code remains present for behavior
preservation, but consumer execution requires an explicit workspace/profile decision.
