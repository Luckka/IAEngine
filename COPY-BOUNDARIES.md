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

The destination intentionally has no `.git`, no historical `.ai-runs`, and no
historical `.ai-state`. Any future runtime-generated `.ai-runs` or `.ai-state`
must be created below the destination only. The copied Git lifecycle code remains
present for behavior preservation, but its execution requires a later human-approved
decision about how an independent Git workspace should be provided.
