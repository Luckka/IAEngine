# CLAUDE.md — how Claude works in this repository

This file governs how a Claude session (this one or a future one) works on OnlineOS
Mobile. It assumes zero memory of any past conversation — everything a session needs is
either in this repository or must be asked for. Read this fully before writing code.

## 0. Read this first, every session

1. [`README.md`](./README.md) — what this repo is, the source-of-truth hierarchy, and
   where things live.
2. Whatever part of [`docs/`](./docs/) covers the feature you're about to touch —
   `REQUIREMENTS.md` for the `REQ-*` IDs, `USER_FLOWS.md` for the journey it belongs
   to, `OPEN_QUESTIONS.md` for whether it's blocked.
3. [`architecture/ARCHITECTURE.md`](./architecture/ARCHITECTURE.md) and any ADR in
   [`architecture/adr/`](./architecture/adr/) relevant to what you're building.
   Also read [`architecture/ENGINEERING_STANDARDS.md`](./architecture/ENGINEERING_STANDARDS.md)
   for the canonical Clean Architecture, SOLID, Clean Code, testing, and responsive UI policy.
4. The actual code already in [`app/`](./app/) for the area you're touching, if any
   exists yet — don't assume the docs are fully implemented; check.

## 1. Non-negotiable rules

0. **Prototype authority is binding.** When a screen/state exists in the rendered
   navigable prototype (`docs/reference/OnlineOS Mobile - Apresentacao (arquivo unico).html`),
   preserve its product composition, navigation, hierarchy, sections, labels,
   actions, statuses, and layout intent. Do not redesign or simplify it. Use
   `docs/PROTOTYPE_SCREEN_MAPPING.json`; stop on `HUMAN_REQUIRED` ambiguity or
   source conflict as defined in `docs/PROTOTYPE_AUTHORITY.md`.

1. **Understand the task before coding.** If a requested change doesn't map to a
   `REQ-*` ID or a named product decision in `docs/`, stop and say so instead of
   guessing what was meant.
2. **Never invent product requirements, API shapes, or backend behavior.** If
   something isn't in `docs/`, it's either in [`docs/OPEN_QUESTIONS.md`](./docs/OPEN_QUESTIONS.md)
   already, or it should be added there — not silently decided in code. This applies
   with extra force to anything in [`docs/API_CONTRACTS.md`](./docs/API_CONTRACTS.md):
   no endpoint, field name, or payload shape gets fabricated to unblock a task. Stub it
   behind the repository interface (`architecture/ARCHITECTURE.md` §1) and say so.
3. **Respect ADRs.** Don't silently switch state-management, persistence, navigation,
   or DI approach mid-feature because it seemed convenient. If an ADR looks wrong for
   the task at hand, say so and propose amending it — don't route around it quietly.
4. **Identify offline implications.** Before implementing any feature that writes
   data, check whether it needs to work offline (most technician-facing writes do —
   see [`docs/OFFLINE_SYNC.md`](./docs/OFFLINE_SYNC.md)) and whether it needs to
   participate in the sync queue (`architecture/ARCHITECTURE.md` §6). Don't ship a
   feature that silently requires connectivity when the product requirement says it
   shouldn't.
5. **Identify security implications.** Tokens, PII, location, and signatures all have
   explicit rules in [`docs/SECURITY.md`](./docs/SECURITY.md). Don't log a token; don't
   store sensitive offline data unencrypted; don't skip the permission-gating logic
   for cost/price fields (REQ-OS-019).
6. **Prefer existing project patterns.** If a prior feature already solved "a screen
   with a paginated list and pull-to-refresh," follow that pattern rather than
   inventing a new one, unless the existing pattern is actually wrong for the new case
   — and if so, say why, not just diverge silently.
7. **Avoid unnecessary abstractions.** The layering in `architecture/ARCHITECTURE.md`
   §1 is a floor, not a ceiling. A feature with no real business logic doesn't need a
   `domain/` layer full of pass-through use-cases just for symmetry.
8. **Write appropriate tests** per `architecture/ARCHITECTURE.md` §13 — at minimum,
   unit tests for anything in `domain/`, especially sync/offline/conflict logic.
   Follow the practical tests-first, regression, integration, and responsive testing
   rules in `architecture/ENGINEERING_STANDARDS.md`; report `NOT_APPLICABLE` with a
   reason rather than fabricating irrelevant tests.
9. **Run static analysis** (`flutter analyze`) before considering work done.
10. **Run tests** (`flutter test`, and `integration_test` where relevant) before
    considering work done.
11. **Inspect the resulting diff.** Read what you actually changed, not just what you
    intended to change, before reporting it as finished.
12. **Report risks and assumptions.** If you made a judgment call because the docs
    didn't cover something, say what you assumed and where — ideally as a new
    `OQ-` entry in `docs/OPEN_QUESTIONS.md`, not just in a PR description that gets lost.

## 2. Architecture boundaries (enforced, not aspirational)

- `presentation/` never imports a data-source type (`dio`, `drift`) directly — only
  `domain/`. See `architecture/ARCHITECTURE.md` §1 for why this specific rule is worth
  defending even when it feels like ceremony.
- Business rules stay backend-owned. If you find yourself writing logic that decides
  *whether* an OS can be finalized, what fields are required, or what a status
  transition means — stop. That's backend configuration/business logic per
  `docs/PRODUCT.md` §5, and the mobile app should be asking the backend for that
  answer (once [OQ-005](./docs/OPEN_QUESTIONS.md#oq-005) /
  [OQ-001](./docs/OPEN_QUESTIONS.md#oq-001) are answered), not hardcoding it.
- The sync queue is the only path for offline-capable writes. Don't add a second,
  feature-specific "retry later" mechanism — extend the shared queue
  (`architecture/ARCHITECTURE.md` §6) instead.

## 3. Never assume another AI-generated implementation is correct

This applies to your own past output as much as anyone else's. Validate against
`docs/REQUIREMENTS.md`, the relevant ADR, and actual test/app behavior — not against
"it compiles" or "the diff looks like what was asked." This project's workflow
(`ai/workflows/FEATURE_DEVELOPMENT.md`) has an independent reviewer (Codex) for exactly
this reason; don't treat that step as a formality.

## 4. When you're blocked

If a task depends on an open question in
[`docs/OPEN_QUESTIONS.md`](./docs/OPEN_QUESTIONS.md) marked **BLOCKING**, don't
build around it with an invented assumption. Say what's blocked and why, and — if the
surrounding structure can still be built safely (e.g., the route skeleton in
ADR-005 while OQ-011 is open) — build only that part, explicitly labeled as
provisional.

## 5. What "done" looks like

See `ai/skills/flutter-feature/SKILL.md` → Definition of Done. Short version: analyzed,
tested, diff-reviewed, requirements traced, risks reported. Not: "the happy path
renders." A mapped UI screen additionally requires Codex conformity review using
both rendered Chrome/CDP reference evidence and Flutter emulator/Patrol evidence;
Claude cannot self-approve that gate.
