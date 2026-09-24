# Deferred Quality Issues

These issues were reviewed during the technology-neutral boundary milestone and
are intentionally not changed yet:

- extract a generic milestone-state persistence contract from the current
  filesystem `RoadmapStateStore`;
- split the broad `EngineHostContext` into stable configuration and adapter
  service groups after a second independent consumer exists;
- separate the OnlineOS compatibility executable and technology implementations
  into an adapter project without breaking current artifact/CLI compatibility;
- replace historical OnlineOS namespace and assembly names only through an
  explicit compatibility/distribution plan;
- introduce a dedicated execution-outcome enum only after consumers agree on the
  compatibility semantics of `CompleteAwaitingApproval`, `Approved`,
  `HumanRequired` and `Failed`.

None of these deferred items blocks generic local composition or InfraSentinel's
current validator consumer proof.
