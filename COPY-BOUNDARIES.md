# Copy Boundaries

```text
SOURCE:
  this IAEngine repository

SOURCE POLICY:
  read-only

ONLINEOS_REFERENCE:
  external OnlineOS repository, read-only and never a build/runtime input

DESTINATION:
  IAEngine consumers and optional compatibility adapter

DESTINATION POLICY:
  independent evolution

FORBIDDEN:
  writes, commits, branches or configuration changes in source
```

The OnlineOS reference is inspected only. No OnlineOS branch, commit, working-tree
file, configuration, prompt, test, state directory, or generated artifact is
changed by IAEngine work.

IAEngine owns its Git history and source files. Runtime-generated `.ai-runs` or
`.ai-state` must be created below the consuming workspace only. The legacy Git
lifecycle implementation remains outside IAEngine.Core for compatibility; the
generic Core exposes only the neutral checkpoint contract and does not push or
merge automatically.
