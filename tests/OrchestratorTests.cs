using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Agents;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class OrchestratorTests
{
    [Fact]
    public async Task CodexPassApproves()
    {
        var sut = Create(reviews: [new ReviewResult(ReviewDecision.Pass, [], "ok")]);
        var result = await sut.ExecuteAsync(TestData.Task(), false);
        Assert.Equal(WorkflowState.Approved, result.Run.State);
    }

    [Fact]
    public async Task FailedValidationTriggersRemediationThenApproval()
    {
        var failed = TestData.Validation(false);
        var passed = TestData.Validation(true);
        var agent = new FakeImplementationAgent();
        var sut = Create(agent, validations: [[failed], [passed]], reviews: [new ReviewResult(ReviewDecision.Pass, [], "ok")]);
        var result = await sut.ExecuteAsync(TestData.Task(), false);
        Assert.Equal(WorkflowState.Approved, result.Run.State);
        Assert.Equal(1, result.Run.RemediationCount);
        Assert.Equal(1, agent.RemediationCalls);
    }

    [Fact]
    public async Task CodexFailTriggersRemediationAndRereview()
    {
        var agent = new FakeImplementationAgent();
        var sut = Create(agent, reviews: [TestData.Review(ReviewDecision.Fail, FindingSeverity.High), new ReviewResult(ReviewDecision.Pass, [], "ok")]);
        var result = await sut.ExecuteAsync(TestData.Task(), false);
        Assert.Equal(WorkflowState.Approved, result.Run.State);
        Assert.Equal(2, result.Run.Reviews.Count);
        Assert.Equal(1, agent.RemediationCalls);
    }

    [Fact]
    public async Task ContextOverflowReducesContextWithoutSpendingReviewRetryBudget()
    {
        var agent = new ContextOverflowThenSuccessAgent();
        var reviewer = new FakeReviewer([
            TestData.Review(ReviewDecision.Fail, FindingSeverity.High),
            new ReviewResult(ReviewDecision.Pass, [], "fixed")]);
        var result = await Create(agent, reviewer: reviewer).ExecuteAsync(TestData.Task(), false);

        Assert.Equal(WorkflowState.Approved, result.Run.State);
        Assert.Equal(2, agent.RemediationCalls);
        Assert.Equal(1, result.Run.EngineeringRemediationCycles);
        Assert.Equal(1, result.Run.RetryCounts[FailureCategory.ContextOverflow.ToString()]);
        Assert.DoesNotContain(FailureCategory.ReviewFailure.ToString(), result.Run.RetryCounts.Keys);
    }

    [Fact]
    public async Task ContinueReconcilesLegacyReviewFailureWhenDiagnosisProvesPromptTooLong()
    {
        var run = TestData.Run();
        run.State = WorkflowState.HumanRequired;
        run.CurrentStage = WorkflowState.HumanRequired;
        run.Routing = TestData.Route();
        run.EngineeringProfile = TestData.Engineering();
        run.ImplementationCompleted = true;
        run.Reviews.Add(TestData.Review(ReviewDecision.Fail, FindingSeverity.High));
        run.LastFailure = TestData.Failure(FailureCategory.ReviewFailure, humanRequired: true, stage: WorkflowState.Remediating) with
        {
            StandardOutput = "{\"terminal_reason\":\"prompt_too_long\",\"api_error_status\":400}"
        };

        var result = await Create(reviews: [new ReviewResult(ReviewDecision.Pass, [], "fixed")]).ContinueAsync(run);

        Assert.Equal(WorkflowState.Approved, result.State);
        Assert.Contains(result.Diagnoses, x => x.Category == FailureCategory.ContextOverflow);
    }

    [Fact]
    public async Task CodexActionableFailAtRemediationCountTwoRemainsAutonomous()
    {
        var sut = Create(maxCycles: 5, reviews:
        [
            TestData.Review(ReviewDecision.Fail, FindingSeverity.High),
            TestData.Review(ReviewDecision.Fail, FindingSeverity.High),
            TestData.Review(ReviewDecision.Fail, FindingSeverity.High),
            new ReviewResult(ReviewDecision.Pass, [], "fixed")
        ]);

        var result = await sut.ExecuteAsync(TestData.Task(), false);

        Assert.Equal(WorkflowState.Approved, result.Run.State);
        Assert.Equal(3, result.Run.RemediationCount);
        Assert.Equal(3, result.Run.EngineeringRemediationCycles);
    }

    [Fact]
    public async Task ChangedFindingsContinueAndProgressExtensionIsAllowed()
    {
        var first = TestData.Review(ReviewDecision.Fail, FindingSeverity.High);
        var changed = first with { Findings = [new ReviewFinding(FindingSeverity.Medium, "other.dart", "2", "different issue", "impact", "fix")] };
        var sut = Create(maxCycles: 5, extensions: 2, reviews: [first, first, first, first, first, changed, new ReviewResult(ReviewDecision.Pass, [], "fixed")]);

        var result = await sut.ExecuteAsync(TestData.Task(), false);

        Assert.Equal(WorkflowState.Approved, result.Run.State);
        Assert.Equal(6, result.Run.RemediationCount);
        Assert.Contains(result.Run.ReviewProgressHistory, signature => signature.Contains("other.dart", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnchangedFindingEventuallyRequiresNonConvergingHumanDecision()
    {
        var finding = TestData.Review(ReviewDecision.Fail, FindingSeverity.High);
        var sut = Create(maxCycles: 5, reviews: [finding, finding, finding, finding, finding, finding]);

        var result = await sut.ExecuteAsync(TestData.Task(), false);

        Assert.Equal(WorkflowState.HumanRequired, result.Run.State);
        Assert.Equal(FailureCategory.NonConvergingRemediation, result.Run.LastFailure!.Category);
        Assert.Equal(5, result.Run.EngineeringRemediationCycles);
        Assert.Contains("same substantive", result.Run.LastFailure.RootCause, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderRateLimitRetriesWithoutConsumingEngineeringBudget()
    {
        var rateLimit = new ReviewResult(ReviewDecision.Fail, [], "HTTP 429", new ProcessResult("codex", 1, "", "HTTP 429", TimeSpan.Zero, ProviderHttpStatus: 429));
        var reviewer = new FakeReviewer([rateLimit, new ReviewResult(ReviewDecision.Pass, [], "ok")]);
        var sut = Create(reviewer: reviewer);

        var result = await sut.ExecuteAsync(TestData.Task(), false);

        Assert.Equal(WorkflowState.Approved, result.Run.State);
        Assert.Equal(0, result.Run.EngineeringRemediationCycles);
        Assert.Equal(1, result.Run.ProviderRecoveryAttempts);
    }

    [Fact]
    public async Task MalformedReviewOutputRetriesWithoutConsumingEngineeringBudget()
    {
        var malformed = new ReviewResult(ReviewDecision.Fail, [], "malformed", IsProtocolFailure: true);
        var reviewer = new FakeReviewer([malformed, new ReviewResult(ReviewDecision.Pass, [], "ok")]);
        var result = await Create(reviewer: reviewer).ExecuteAsync(TestData.Task(), false);

        Assert.Equal(WorkflowState.Approved, result.Run.State);
        Assert.Equal(0, result.Run.EngineeringRemediationCycles);
        Assert.Equal(1, result.Run.ProtocolRecoveryAttempts);
    }

    [Fact]
    public async Task RecoveredReviewFailureIsHistoricalAndNotCurrentAfterApproval()
    {
        var sut = Create(reviews: [TestData.Review(ReviewDecision.Fail, FindingSeverity.High), new ReviewResult(ReviewDecision.Pass, [], "ok")]);

        var result = await sut.ExecuteAsync(TestData.Task(), false);

        Assert.Equal(WorkflowState.Approved, result.Run.State);
        Assert.Equal("PASS", result.Run.FinalDecision);
        Assert.Null(result.Run.LastFailure);
        Assert.Null(result.Run.ActiveRecovery);
        Assert.Null(result.Run.ResumeStage);
        Assert.Null(result.Run.ResumeAfter);
        Assert.Equal(WorkflowState.Approved, result.Run.CurrentStage);
        Assert.Single(result.Run.Diagnoses);
        Assert.Equal(1, result.Run.RemediationCount);
        Assert.Equal(2, result.Run.CurrentValidationCycle);
        Assert.Equal(2, result.Run.CurrentReviewCycle);
        Assert.Equal(ReviewDecision.Pass, result.Run.Reviews[^1].Decision);
    }

    [Fact]
    public async Task MaximumCyclesEndsAtHumanRequired()
    {
        var sut = Create(reviews:
        [
            TestData.Review(ReviewDecision.Fail, FindingSeverity.High),
            TestData.Review(ReviewDecision.Fail, FindingSeverity.High),
            TestData.Review(ReviewDecision.Fail, FindingSeverity.High)
        ], maxCycles: 2);
        var result = await sut.ExecuteAsync(TestData.Task(), false);
        Assert.Equal(WorkflowState.HumanRequired, result.Run.State);
        Assert.Equal(2, result.Run.RemediationCount);
    }

    [Fact]
    public async Task OrdinaryContinueRefusesExceptionalHumanRequiredWithoutRunningAgents()
    {
        var reviewer = new FakeReviewer([new ReviewResult(ReviewDecision.Pass, [], "must not run")]);
        var run = TestData.Run();
        run.State = WorkflowState.HumanRequired;
        run.LastFailure = TestData.Failure(FailureCategory.ProductAmbiguity);

        var result = await Create(reviewer: reviewer).ContinueAsync(run);

        Assert.Equal(WorkflowState.HumanRequired, result.State);
        Assert.Equal(0, reviewer.Calls);
    }

    [Fact]
    public async Task LegacyRoutineHumanRequiredReviewAutomaticallyReopensAndContinues()
    {
        var run = TestData.Run();
        run.State = WorkflowState.HumanRequired;
        run.CurrentStage = WorkflowState.HumanRequired;
        run.Routing = TestData.Route();
        run.EngineeringProfile = TestData.Engineering();
        run.ImplementationCompleted = true;
        run.RemediationCount = 2;
        run.Reviews.Add(TestData.Review(ReviewDecision.Fail, FindingSeverity.High));
        run.Transitions.Add(new StateTransition(WorkflowState.Reviewing, WorkflowState.HumanRequired, DateTimeOffset.UtcNow, "Maximum remediation cycles reached."));
        var reviewer = new FakeReviewer([new ReviewResult(ReviewDecision.Pass, [], "fixed")]);

        var result = await Create(reviewer: reviewer, maxCycles: 5).ContinueAsync(run);

        Assert.Equal(WorkflowState.Approved, result.State);
        Assert.Equal(1, reviewer.Calls);
        Assert.Contains(result.Transitions, x => x.From == WorkflowState.HumanRequired && x.To == WorkflowState.Validating);
        Assert.Equal(2, result.RemediationCount);
        Assert.Equal(2, result.EngineeringRemediationCycles);
    }

    [Fact]
    public async Task AuthorizedReviewingProviderRetryRevalidatesAndReusesImplementationAndHistory()
    {
        var run = TestData.Run();
        run.State = WorkflowState.HumanRequired;
        run.LastFailure = TestData.Failure(FailureCategory.ProviderRateLimit, stage: WorkflowState.Reviewing);
        run.Routing = TestData.Route();
        run.EngineeringProfile = TestData.Engineering();
        run.ImplementationCompleted = true;
        run.ValidationCompleted = true;
        run.ReviewCompleted = true;
        run.ValidationResults.Add(TestData.Validation(true));
        run.Reviews.Add(TestData.Review(ReviewDecision.Fail, FindingSeverity.High));
        run.Diagnoses.Add(run.LastFailure);
        var oldTransition = new StateTransition(WorkflowState.Reviewing, WorkflowState.HumanRequired, DateTimeOffset.UtcNow.AddMinutes(-1), "transient retry exhausted");
        run.Transitions.Add(oldTransition);
        run.RetryCounts[FailureCategory.ProviderRateLimit.ToString()] = 3;
        run.RetryCounts["unrelated"] = 7;
        var implementation = new FakeImplementationAgent();
        var reviewer = new FakeReviewer([new ReviewResult(ReviewDecision.Pass, [], "ok")]);

        var result = await Create(implementation, reviewer: reviewer).RetryHumanRequiredAsync(run);

        Assert.Equal(WorkflowState.Approved, result.State);
        Assert.Equal(0, implementation.ImplementationCalls);
        Assert.Equal(1, reviewer.Calls);
        Assert.Contains(oldTransition, result.Transitions);
        Assert.Contains(result.Diagnoses, x => x.Category == FailureCategory.ProviderRateLimit);
        Assert.Equal(7, result.RetryCounts["unrelated"]);
        Assert.DoesNotContain(FailureCategory.ProviderRateLimit.ToString(), result.RetryCounts.Keys);
        Assert.Contains(result.Transitions, x => x.From == WorkflowState.HumanRequired && x.To == WorkflowState.Validating);
    }

    [Fact]
    public async Task AuthorizedRemediatingProviderRetryRemainsSupported()
    {
        var run = TestData.Run();
        run.State = WorkflowState.HumanRequired;
        run.LastFailure = TestData.Failure(FailureCategory.ProviderRateLimit, humanRequired: true, stage: WorkflowState.Remediating);
        run.Routing = TestData.Route();
        run.EngineeringProfile = TestData.Engineering();
        run.ImplementationCompleted = true;
        run.ReviewCompleted = true;
        run.Reviews.Add(TestData.Review(ReviewDecision.Fail, FindingSeverity.High));
        var implementation = new FakeImplementationAgent();
        var reviewer = new FakeReviewer([new ReviewResult(ReviewDecision.Pass, [], "ok")]);

        var result = await Create(implementation, reviewer: reviewer).RetryHumanRequiredAsync(run);

        Assert.Equal(WorkflowState.Approved, result.State);
        Assert.Equal(0, implementation.ImplementationCalls);
        Assert.Equal(1, reviewer.Calls);
    }

    [Theory]
    [InlineData(FailureCategory.PermissionDenied, WorkflowState.Reviewing)]
    [InlineData(FailureCategory.ProviderStructuredRefusal, WorkflowState.Reviewing)]
    [InlineData(FailureCategory.ProviderRateLimit, WorkflowState.Implementing)]
    public async Task AuthorizedRetryRejectsUnsafeOrWrongStageRecovery(FailureCategory category, WorkflowState stage)
    {
        var run = TestData.Run();
        run.State = WorkflowState.HumanRequired;
        run.LastFailure = TestData.Failure(category, humanRequired: true, stage: stage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create().RetryHumanRequiredAsync(run));

        Assert.Equal(WorkflowState.HumanRequired, run.State);
        Assert.Empty(run.Transitions);
    }

    [Fact]
    public async Task ImplementationFailureEndsFailed()
    {
        var sut = Create(new FakeImplementationAgent(false, "CLAUDE_PERMISSION_DENIED"));
        var result = await sut.ExecuteAsync(TestData.Task(), false);
        Assert.Equal(WorkflowState.Failed, result.Run.State);
        Assert.Equal("CLAUDE_PERMISSION_DENIED", result.Run.Transitions[^1].Reason);
    }

    [Fact]
    public async Task OptionalPermissionDenialWithProducedChangesContinuesAndIsPersisted()
    {
        var denial = new PermissionDenial("Bash", "{\"tool_name\":\"Bash\"}", false);
        var implementation = new ImplementationResult(true, "implemented", ["file.md"], PermissionDenials: [denial], Warnings: ["optional denial"]);
        var agent = new FakeImplementationAgent(implementationResult: implementation);
        var reviewer = new FakeReviewer([new ReviewResult(ReviewDecision.Pass, [], "ok")]);
        var git = new FakeGit(["", "", " M file.md"], ["", "changed diff"]);
        var sut = Create(agent, reviewer: reviewer, git: git);

        var result = await sut.ExecuteAsync(TestData.Task(), false);

        Assert.Equal(WorkflowState.Approved, result.Run.State);
        Assert.Equal(1, reviewer.Calls);
        Assert.Equal(denial, Assert.Single(result.Run.PermissionDenials));
        Assert.Contains("optional denial", result.Run.Warnings);
    }

    [Fact]
    public async Task OptionalPermissionDenialWithoutProducedChangesFails()
    {
        var denial = new PermissionDenial("Bash", "{\"tool_name\":\"Bash\"}", false);
        var implementation = new ImplementationResult(true, "implemented", ["file.md"], PermissionDenials: [denial]);
        var sut = Create(new FakeImplementationAgent(implementationResult: implementation));

        var result = await sut.ExecuteAsync(TestData.Task(), false);

        Assert.Equal(WorkflowState.Failed, result.Run.State);
        Assert.Equal(ClaudeAgent.PermissionDeniedReason, result.Run.Transitions[^1].Reason);
    }

    [Fact]
    public async Task DryRunDoesNotCallImplementationOrReview()
    {
        var agent = new FakeImplementationAgent();
        var reviewer = new FakeReviewer([new ReviewResult(ReviewDecision.Pass, [], "ok")]);
        var sut = Create(agent, reviewer: reviewer);
        var result = await sut.ExecuteAsync(TestData.Task(), true);
        Assert.NotNull(result.Plan);
        Assert.Equal(0, agent.ImplementationCalls);
        Assert.Equal(0, reviewer.Calls);
    }

    [Fact]
    public async Task ReadOnlyReferenceValidationCompletesWithoutImplementationPipeline()
    {
        var agent = new FakeImplementationAgent();
        var reviewer = new FakeReviewer([new ReviewResult(ReviewDecision.Pass, [], "must not run")]);
        var task = new DevelopmentTask("M3-REFERENCE-VALIDATION", "Read-only OnlineOS reference validation", "Validate monolith reference roots without implementation.");
        var result = await Create(agent, reviewer: reviewer).ExecuteAsync(task, false);
        Assert.Equal(WorkflowState.Completed, result.Run.State);
        Assert.Equal("REFERENCE_VALIDATED", result.Run.FinalDecision);
        Assert.Equal(0, agent.ImplementationCalls);
        Assert.Equal(0, reviewer.Calls);
    }

    [Fact]
    public async Task EngineeringProfileAndValidationExpectationsArePersistedForAudit()
    {
        var store = new FakeRunStore();
        var sut = Create(runStore: store);

        var result = await sut.ExecuteAsync(TestData.Task(), false);

        Assert.NotNull(result.Run.EngineeringProfile);
        Assert.Contains("engineering-profile.json", store.ArtifactNames);
        Assert.Contains("validation-expectations.json", store.ArtifactNames);
        Assert.Contains(result.Run.ValidationExpectations, expectation => expectation.Applicability == StandardApplicability.Required && !expectation.Covered);
        Assert.Contains(result.Run.Warnings, warning => warning.Contains("Required deterministic validation category is not configured", StringComparison.Ordinal));
        Assert.NotEmpty(result.Run.ValidationResults);
    }

    [Fact]
    public async Task DeterministicValidationFailurePreventsReviewEvenIfReviewerWouldPass()
    {
        var reviewer = new FakeReviewer([new ReviewResult(ReviewDecision.Pass, [], "ok")]);
        var sut = Create(validations: [[TestData.Validation(false)]], reviewer: reviewer, maxCycles: 0);

        var result = await sut.ExecuteAsync(TestData.Task(), false);

        Assert.Equal(WorkflowState.HumanRequired, result.Run.State);
        Assert.Equal(0, reviewer.Calls);
        Assert.False(result.Run.ValidationResults[^1].Passed);
    }

    [Fact]
    public async Task StructuredReviewerQualityIsRetainedInRunAudit()
    {
        var quality = new QualityAssessment("architecture", QualityStatus.Pass, "boundaries respected");
        var review = new ReviewResult(ReviewDecision.Pass, [], "ok", Quality: [quality]);
        var sut = Create(reviews: [review]);

        var result = await sut.ExecuteAsync(TestData.Task(), false);

        Assert.Equal(quality, Assert.Single(result.Run.Reviews).Quality!.Single());
        Assert.Equal("PASS", result.Run.FinalDecision);
    }

    [Fact]
    public async Task ContinueFromImplementingWithCompletedArtifactSkipsImplementation()
    {
        var agent = new FakeImplementationAgent();
        var run = TestData.Run();
        run.State = WorkflowState.Implementing;
        run.CurrentStage = WorkflowState.Implementing;
        run.Routing = TestData.Route();
        run.EngineeringProfile = TestData.Engineering();
        run.ImplementationCompleted = true;
        var sut = Create(agent);

        var result = await sut.ContinueAsync(run);

        Assert.Equal(WorkflowState.Approved, result.State);
        Assert.Equal(0, agent.ImplementationCalls);
    }

    [Fact]
    public async Task ContinueFromReviewingWithCompletedPassSkipsCodex()
    {
        var reviewer = new FakeReviewer([new ReviewResult(ReviewDecision.Pass, [], "already persisted")]);
        var run = TestData.Run();
        run.State = WorkflowState.Reviewing;
        run.CurrentStage = WorkflowState.Reviewing;
        run.Routing = TestData.Route();
        run.EngineeringProfile = TestData.Engineering();
        run.LatestValidationResults.Add(TestData.Validation(true));
        run.Reviews.Add(new ReviewResult(ReviewDecision.Pass, [], "persisted"));
        run.ReviewCompleted = true;
        var sut = Create(reviewer: reviewer);

        var result = await sut.ContinueAsync(run);

        Assert.Equal(WorkflowState.Approved, result.State);
        Assert.Equal(0, reviewer.Calls);
    }

    [Fact]
    public async Task NewRunPersistsCreatedBeforeRoutingAndUsesPersistedStageTransitions()
    {
        var store = new FakeRunStore();
        var result = await Create(runStore: store).ExecuteAsync(TestData.Task(), false);

        Assert.Equal(WorkflowState.Created, store.SavedStates[0]);
        Assert.Contains(WorkflowState.Routing, store.SavedStates);
        Assert.Contains(result.Run.Transitions, transition => transition.To == WorkflowState.Implementing);
    }

    [Fact]
    public async Task NewRunStoppedAtImplementationCanContinueThroughTheSameEngine()
    {
        var store = new FakeRunStore();
        var interrupted = Create(new CancelingImplementationAgent(), runStore: store);
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => interrupted.ExecuteAsync(TestData.Task(), false));

        Assert.NotNull(exception);
        var persisted = store.LastRun!;
        Assert.Equal(WorkflowState.Implementing, persisted.State);
        Assert.False(persisted.ImplementationCompleted);

        var resumedAgent = new FakeImplementationAgent();
        var resumed = await Create(resumedAgent, runStore: store).ContinueAsync(persisted);

        Assert.Equal(WorkflowState.Approved, resumed.State);
        Assert.Equal(1, resumedAgent.ImplementationCalls);
    }

    private static Orchestrator Create(
        IImplementationAgent? agent = null,
        IReadOnlyList<IReadOnlyList<ValidationResult>>? validations = null,
        IReadOnlyList<ReviewResult>? reviews = null,
        FakeReviewer? reviewer = null,
        FakeGit? git = null,
        FakeRunStore? runStore = null,
        int maxCycles = 2,
        int extensions = 0)
    {
        var options = new AppOptions
        {
            Orchestrator = new OrchestratorOptions
            {
                MaxRemediationCycles = maxCycles,
                EngineeringRemediationCycles = maxCycles,
                EngineeringProgressExtensions = extensions,
                DeterministicValidationRemediationCycles = maxCycles
            }
        };
        return new Orchestrator(new FakeRouter(), agent ?? new FakeImplementationAgent(), new FakeValidation(validations ?? [[TestData.Validation(true)]]),
            reviewer ?? new FakeReviewer(reviews ?? [new ReviewResult(ReviewDecision.Pass, [], "ok")]), git ?? new FakeGit(), runStore ?? new FakeRunStore(),
            new ReviewPolicy(options.ReviewPolicy), new WorkflowStateMachine(), options);
    }

    private sealed class FakeRouter : ITaskRouter
    {
        public Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, true, "ok"));
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default) => Task.FromResult(TestData.Route());
    }

    private sealed class FakeImplementationAgent(
        bool success = true,
        string summary = "implementation",
        ImplementationResult? implementationResult = null) : IImplementationAgent
    {
        public int ImplementationCalls { get; private set; }
        public int RemediationCalls { get; private set; }
        public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default)
        { ImplementationCalls++; return Task.FromResult(implementationResult ?? new ImplementationResult(success, summary, [])); }
        public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0)
        { RemediationCalls++; return Task.FromResult(new ImplementationResult(success, "remediation", [])); }
    }

    private sealed class ContextOverflowThenSuccessAgent : IImplementationAgent
    {
        public int RemediationCalls { get; private set; }
        public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImplementationResult(true, "implementation", []));
        public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0)
        {
            RemediationCalls++;
            if (contextReductionAttempt == 0)
                return Task.FromResult(new ImplementationResult(false, "provider rejected request", [], new ProcessResult("claude", 1, "{\"terminal_reason\":\"prompt_too_long\"}", "", TimeSpan.Zero), FailureCode: ClaudeAgent.PromptTooLongReason));
            return Task.FromResult(new ImplementationResult(true, "remediated", []));
        }
    }

    private sealed class CancelingImplementationAgent : IImplementationAgent
    {
        public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException(cancellationToken);

        public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0)
            => throw new OperationCanceledException(cancellationToken);
    }

    private sealed class FakeValidation(IReadOnlyList<IReadOnlyList<ValidationResult>> results) : IValidationRunner
    {
        private int index;
        public int Calls { get; private set; }
        public Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(results[Math.Min(index++, results.Count - 1)]); }
    }

    private sealed class FakeReviewer(IReadOnlyList<ReviewResult> results) : IReviewAgent
    {
        private int index;
        public int Calls { get; private set; }
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(results[Math.Min(index++, results.Count - 1)]); }
    }

    private sealed class FakeGit(IReadOnlyList<string>? statuses = null, IReadOnlyList<string>? diffs = null) : IGitService
    {
        private int statusIndex;
        private int diffIndex;
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, "ok"));
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/test");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("abc");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default)
        {
            var values = diffs ?? ["diff"];
            return Task.FromResult(values[Math.Min(diffIndex++, values.Count - 1)]);
        }
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult("/repo");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            var values = statuses ?? [""];
            return Task.FromResult(values[Math.Min(statusIndex++, values.Count - 1)]);
        }
    }

    private sealed class FakeRunStore : IRunStore
    {
        public List<string> ArtifactNames { get; } = [];
        public List<WorkflowState> SavedStates { get; } = [];
        public RunRecord? LastRun { get; private set; }
        public async Task InitializeAsync(RunRecord run, CancellationToken cancellationToken = default) => await SaveAsync(run, cancellationToken);
        public Task SaveArtifactAsync<T>(string runId, string name, T value, CancellationToken cancellationToken = default)
        {
            ArtifactNames.Add(name);
            return Task.CompletedTask;
        }
        public Task SaveAsync(RunRecord run, CancellationToken cancellationToken = default)
        {
            SavedStates.Add(run.State);
            LastRun = run;
            return Task.CompletedTask;
        }
    }
}
