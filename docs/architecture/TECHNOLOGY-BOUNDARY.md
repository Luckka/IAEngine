# Technology-Neutral Engine Boundary

## Boundary

The generic Engine boundary is composed of contracts and mechanics that do not
choose a programming language, cloud provider, product, or execution device:

- `EngineHost` and `EngineHostContext`;
- runtime composition and explicit provider/capability registration;
- `Orchestrator`, workflow state machine, retries and recovery;
- `MilestoneRunner`, roadmap validation and task dependency ordering;
- `IRunStore`, `IGitService`, task/milestone sources and progress contracts;
- structured execution results and persisted run/milestone state.

The Engine does not infer an adapter from a project name, path or environment.
Missing declared components fail before factories and workflow side effects.

## Adapters and consumers

Technology or product behavior belongs behind explicit consumer registrations:

- validators implement `IValidationRunner`;
- providers implement the stage-specific contracts such as `ITaskRouter`,
  `IImplementationAgent` and `IReviewAgent`;
- project policies implement consumer-owned policy contracts or named composition
  registrations;
- task and milestone sources implement `IEngineTaskSource` and
  `IEngineMilestoneSource`;
- QA, Flutter, Patrol, ADB, AWS and product reference integrations remain
  adapters, not generic Engine defaults.

## OnlineOS compatibility classification

The repository still contains a compatibility host and technology implementations
copied from the original OnlineOS orchestrator. They are classified as temporary
compatibility/adapter code, not as the generic host contract:

- `Program.cs`, `Agents/`, `Reference/`, Patrol/ADB pipeline code and
  `ValidationRunner` retain OnlineOS or Flutter behavior;
- `appsettings.json`, QA options and reference configuration retain explicit
  OnlineOS profile values;
- prompts, prototype evidence and `b1208` reference behavior remain project policy;
- generic host tests use local fakes and do not activate these implementations.

The two tests that required the absent OnlineOS Flutter application were removed
from the main Engine test suite. Remaining Flutter characterization tests cover
compatibility behavior with fakes and are tracked for a future adapter split.

## Test organization

Generic tests must exercise composition, host execution, milestone dependency
validation, persistence isolation, fail-closed resolution, cancellation,
recovery and approval using local fakes. Technology-specific tests belong in an
adapter or consumer test project and must not require an application tree absent
from the Engine repository.

## Current removals and deferred work

Removed from the main suite:

- `FlutterProfileRunsEachConfiguredCategoryFromAppRoot`;
- `IntegrationWithoutTargetIsExplicitlyNotExecutable`.

Deferred: moving all Flutter/Patrol/reference implementations into a separately
owned OnlineOS adapter. That is larger than this boundary milestone and would
require an approved compatibility/distribution plan.

## Learning Checkpoint

1. A Flutter test that requires `app/` belongs to the Flutter adapter/consumer, not the generic Engine suite.
2. Core owns workflow mechanics; adapters own technology behavior; providers own stage integrations; validators own deterministic checks.
3. A Node or Angular project can consume the same Engine by registering its own providers, validators, task source, milestone source and persistence/Git adapters.
4. Removing an invalid test is different from masking a failure: the test is removed because its required system is outside the Engine boundary, while the generic behavior is preserved in boundary tests.
5. InfraSentinel retains infrastructure rules, findings and severity semantics.
6. SOLID and Clean Architecture keep policy and technology dependencies pointing toward explicit contracts instead of embedding them in the orchestration core.
