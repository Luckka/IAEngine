# AGENTS.md — instructions for AI coding agents working on OnlineOS Mobile

## Public IAEngine repository contribution policy

This checkout is the public IAEngine baseline. Future changes must:

- use Conventional Commit messages;
- keep each commit small, logically coherent, and testable;
- avoid empty, cosmetic, or history-rewriting commits;
- preserve the external OnlineOS source as read-only and never publish local paths,
  runtime state, credentials, logs, or generated artifacts;
- record the verification commands and relevant architectural decisions with the
  change when applicable.

These instructions guide repository work only; they are not runtime configuration
and are not consumed by the orchestrator.

This file is model-independent. It's read by any AI coding agent working on this
repository — Claude, Codex, or anything else — not just one vendor's tool. If you're
looking for Claude-specific workflow notes, see [`CLAUDE.md`](./CLAUDE.md); everything
in this file applies regardless of which agent is reading it.

## Project purpose

OnlineOS Mobile is a Flutter (iOS + Android) mobile app for OnlineOS, an existing
field-service-management platform. It serves two profiles — Field Technician and
Customer/Requester — and integrates with the existing backend via API rather than
reimplementing its business rules. Offline operation is a first-class requirement, not
a fallback. Full product context: [`docs/PRODUCT.md`](./docs/PRODUCT.md).

## Repository map

```
onlineos-mobile/
├── app/                  Flutter source (empty until Milestone 1 — see app/README.md)
├── docs/                 Product knowledge, normalized from client-supplied material
│   └── reference/        Original, unmodified source documents (the RFP, the UX package)
├── architecture/         Architecture proposal + Architecture Decision Records
│   └── adr/
├── ai/
│   ├── agents/           Role definitions for this project's AI agents
│   ├── skills/           Reusable, repeatable procedures (e.g. "implement a Flutter feature")
│   └── workflows/        How the human + multiple AI agents collaborate end to end
├── tests/                Test suites (structure follows architecture/ARCHITECTURE.md §13)
├── CLAUDE.md             Claude-specific session instructions
└── AGENTS.md             This file
```

## Source-of-truth hierarchy

When a decision or a fact is ambiguous, resolve it in this order — don't skip to a
lower level because it's more convenient:

1. **`docs/reference/`** — original client-supplied evidence (the RFP, the UX/design
   package). Unmodified. If everything else disagrees with this, this wins.
2. **`docs/`** — normalized product knowledge derived from #1. This is what you should
   actually read day to day.
3. **`architecture/`** — architecture decisions derived from #2.
4. **`architecture/adr/`** — the specific, dated rationale behind a decision in #3.
5. **`app/`** — the actual implementation, which must respect all of the above.

If code and documentation disagree, that's a bug to flag and investigate — not
something to silently resolve by trusting whichever one you happened to read first.

## Prototype authority policy

For any screen/state represented by the rendered navigable HTML prototype at
[`docs/reference/OnlineOS Mobile - Apresentacao (arquivo unico).html`](./docs/reference/OnlineOS%20Mobile%20-%20Apresentacao%20(arquivo%20unico).html), the prototype has product-design authority. Agents must preserve its structure, hierarchy, navigation, sections, components, labels, actions, statuses, and layout intent; they must not redesign, simplify, or substitute it. See [`docs/PROTOTYPE_AUTHORITY.md`](./docs/PROTOTYPE_AUTHORITY.md) and [`docs/PROTOTYPE_SCREEN_MAPPING.json`](./docs/PROTOTYPE_SCREEN_MAPPING.json).

If no corresponding prototype state exists, inspect the product and design sources
before deciding. A product-visible ambiguity is `HUMAN_REQUIRED / PRODUCT_DESIGN_AMBIGUITY`;
an authority conflict is `HUMAN_REQUIRED / AUTHORITATIVE_SOURCE_CONFLICT`.

## Architecture rules

Full detail in [`architecture/ARCHITECTURE.md`](./architecture/ARCHITECTURE.md); the
canonical cross-cutting code-quality, testing, and responsive/adaptive UI policy is
[`architecture/ENGINEERING_STANDARDS.md`](./architecture/ENGINEERING_STANDARDS.md).
Apply its standards pragmatically and record non-applicable categories explicitly. The
two rules worth repeating here because they're the ones most likely to get silently
violated under time pressure:

- **`presentation/` never talks to `dio` or `drift` directly** — only to `domain/`.
- **Business rules stay backend-owned.** The app renders what the backend says is
  required/valid/allowed; it does not decide those things locally. See
  [`docs/PRODUCT.md`](./docs/PRODUCT.md) §5.

## Testing expectations

- Every `domain/` unit (use-cases, sync-queue logic, conflict detection) gets unit
  tests. This is where correctness actually lives in an offline-first app — prioritize
  it over UI polish.
- Every reusable design-system widget gets a widget test.
- Offline/sync flows get `integration_test` coverage that actually drives the sync
  engine against a faked backend, not just a unit test of one function in isolation.
- Full testing strategy: `architecture/ARCHITECTURE.md` §13.

## Documentation expectations

- A new `REQ-*` ID gets added to [`docs/REQUIREMENTS.md`](./docs/REQUIREMENTS.md) if a
  human clarifies a genuinely new requirement mid-implementation — don't let a
  requirement exist only in a commit message or a chat transcript.
- A new architecture decision of real weight (a new major dependency, a change to a
  layering rule, a new cross-cutting subsystem) gets an ADR, following the format in
  any existing file under `architecture/adr/`. Not every small decision needs one —
  see `architecture/ARCHITECTURE.md`'s own framing: justify a dependency against the
  requirement it satisfies, don't ADR every `pubspec.yaml` addition.
- An assumption made to unblock work in the absence of a confirmed answer gets logged
  as an entry in [`docs/OPEN_QUESTIONS.md`](./docs/OPEN_QUESTIONS.md), classified and
  marked blocking or non-blocking — not left implicit in code.

## Commands agents should run

(Populated as the Flutter project is scaffolded in Milestone 1 — `app/README.md`
tracks the actual current state. Expected baseline once the project exists:)

- `flutter analyze` — static analysis; must be clean before work is considered done.
- `flutter test` — unit + widget tests.
- `flutter test integration_test` — integration suite.
- `dart run build_runner build --delete-conflicting-outputs` — regenerate Drift/Riverpod
  code after touching a table definition or a `@riverpod`-annotated provider.

## Prohibited behaviors

- Inventing API endpoints, payload shapes, or backend business rules not present in
  `docs/` or explicitly stubbed and flagged as such.
- Silently resolving a contradiction between source documents or between docs and code
  — flag it in `docs/OPEN_QUESTIONS.md` instead.
- Treating a BLOCKING open question as answered by assumption.
- Adding a dependency (state management, DI, persistence, navigation, or otherwise)
  that duplicates something an existing ADR already decided, without proposing to
  amend that ADR first.
- Merging to `main`, deploying, accessing production secrets/databases, or deleting
  production resources — see §"AI development security" below.
- Implementing features prematurely: this repository's Milestone 0 scope
  (`README.md`) is documentation and foundation only — treat any request to build full
  application features against this milestone's state as out of scope unless a human
  has explicitly advanced the project to a later milestone.

## AI development security

Agents operate under these permission boundaries regardless of what tooling access
they technically have available:

**Allowed, once human-approved infrastructure exists for it:**
read the repository; modify a feature branch; run local commands (analyze, test,
build); inspect logs; create a pull request.

**Not allowed without explicit human-controlled infrastructure, ever:**
merge to `main`; deploy to production; access production secrets; modify production
databases; delete production resources.

Human approval is the deployment/security gate. No agent grants itself broader access
than this by finding a workaround.

## The critical principle

**Never assume another AI-generated implementation is correct.** Validate it against
`docs/REQUIREMENTS.md`, the relevant ADR, and actual running behavior — not against
whether it compiles or superficially matches what was asked. This holds whether the
prior implementation came from Claude, Codex, or any other agent, including your own
earlier output in the same session.

## Definition of Done

A feature is done when, and only when, all of the following are true — see
`ai/skills/flutter-feature/SKILL.md` for the full workflow this comes out of:

1. It's traceable to a `REQ-*` ID (or an explicit human instruction that isn't yet in
   `docs/REQUIREMENTS.md`, in which case that gap gets filed too).
2. It respects the architecture boundaries above and every relevant ADR.
3. `flutter analyze` is clean.
4. Relevant unit/widget/integration tests exist and pass.
5. The diff has actually been read, not just generated.
6. Assumptions and risks are reported — in `docs/OPEN_QUESTIONS.md` if they're
   product/backend-facing, in the PR description if they're implementation detail worth
   a reviewer's attention.
7. An independent review (human or a second AI agent per
   `ai/workflows/FEATURE_DEVELOPMENT.md`) has actually looked at it — self-certification
   is not sufficient for anything beyond the smallest fix.
8. If a prototype mapping exists, Codex has reviewed rendered prototype and Flutter
   emulator/Patrol evidence for conformity; BLOCKER or MAJOR findings fail the work.
