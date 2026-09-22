# M1 — Decisions

## Selected for this milestone

1. **VERIFIED IN CODE / RECOMMENDED:** use a small JSON-compatible project profile as an additive configuration boundary.
2. **VERIFIED IN CODE:** keep one executable project and existing namespaces, contracts, providers, commands, prompts, state machine and persistence schema.
3. **VERIFIED IN CODE:** register the current OnlineOS values explicitly in the checked-in compatibility JSON; do not turn them into generic profile defaults.
4. **VERIFIED IN CODE:** use source inspection and assembly inspection for the first isolation tests; no architecture-testing framework was added.
5. **VERIFIED IN CODE:** preserve the current two baseline validation failures as documented behavior; tests were not weakened or changed.
6. **VERIFIED IN CODE:** generic option/model defaults are neutral; compatibility values are supplied by an explicitly declared `Project.Id` of `onlineos-mobile`.
7. **VERIFIED IN CODE:** missing/incomplete profiles fail before provider construction, and generic profiles expose a no-OnlineOS composition plan rather than silently loading validators, QA, reference inspection or policies.
8. **VERIFIED IN CODE:** provider/capability registration is declarative under `Project.Composition`; it is intentionally a small plan contract, not plugin discovery or a generic provider runtime.
9. **VERIFIED IN CODE:** configuration never falls back to the executable binary directory; explicit override and repository-local paths have deterministic precedence.

## Deferred

- **HUMAN DECISION REQUIRED:** rename or preserve the `OnlineOs.AiOrchestrator` assembly/namespace.
- **HUMAN DECISION REQUIRED:** split assemblies or publish packages/tools.
- **HUMAN DECISION REQUIRED:** move prompts and OnlineOS policy to a project context provider.
- **HUMAN DECISION REQUIRED:** extract validator/policy contracts and the compatibility composition root.
- **HUMAN DECISION REQUIRED:** replace the current composition plan with actual project/provider contracts when a second consumer exists.
- **RECOMMENDED:** keep `IProjectAdapter`, plugin discovery, CodeUp and FinGuard out of M1.

## Risks

- **INFERRED:** current external consumers may depend on CLI wording and artifact schemas; a future extraction must add golden compatibility tests first.
- **UNKNOWN:** no consumer inventory exists in the destination.
- **HUMAN DECISION REQUIRED:** whether the two baseline validation failures are waived, fixed in a later milestone, or assigned to the OnlineOS consumer test suite.
- **INFERRED:** compatibility implementation units still contain OnlineOS literals and prompts; their ownership remains deferred, but generic host composition no longer activates them.
