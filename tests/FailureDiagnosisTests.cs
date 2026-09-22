using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class FailureDiagnosisTests
{
    [Fact]
    public void RateLimitIsProviderFailureEvenDuringRemediation()
    {
        var process = new ProcessResult("claude", 1, "", "HTTP 429 usage limit; retry after 30", TimeSpan.Zero, Provider: "anthropic", ProviderHttpStatus: 429, RetryAfter: TimeSpan.FromSeconds(30));
        Assert.Equal(FailureCategory.ProviderRateLimit, FailureClassifier.Classify(WorkflowState.Remediating, process, message: "remediation failed"));
    }

    [Fact]
    public void PromptTooLongStructuredProviderFailureIsNotReviewFailure()
    {
        var process = new ProcessResult("claude", 1,
            "{\"terminal_reason\":\"prompt_too_long\",\"api_error_status\":400}", "", TimeSpan.Zero,
            Provider: "anthropic", ProviderHttpStatus: 400);
        Assert.Equal(FailureCategory.ContextOverflow, FailureClassifier.Classify(WorkflowState.Remediating, process, message: "Claude request failed"));
    }

    [Fact]
    public void ContextOverflowHasIndependentBoundedReductionBudget()
    {
        var options = new OrchestratorOptions { RemediationContext = new RemediationContextOptions { MaxReductionAttempts = 2 } };
        var policy = new RecoveryPolicy(options);
        Assert.False(policy.Decide(Context(FailureCategory.ContextOverflow, 0)).HumanRequired);
        Assert.True(policy.Decide(Context(FailureCategory.ContextOverflow, 2)).HumanRequired);
    }

    [Fact]
    public void TimeoutAndMalformedOutputAreDeterministic()
    {
        Assert.Equal(FailureCategory.ProviderTimeout, FailureClassifier.Classify(WorkflowState.Reviewing, new ProcessResult("codex", 1, "", "timed out", TimeSpan.Zero, true)));
        Assert.Equal(FailureCategory.ProviderMalformedOutput, FailureClassifier.Classify(WorkflowState.Implementing, message: "Claude returned malformed structured output."));
    }

    [Fact]
    public void RequiredPermissionAndProductAmbiguityRequireHuman()
    {
        var options = new OrchestratorOptions { BackoffInitialSeconds = 0 };
        var policy = new RecoveryPolicy(options);
        var permission = Context(FailureCategory.PermissionDenied);
        var ambiguity = Context(FailureCategory.ProductAmbiguity);
        Assert.True(policy.Decide(permission).HumanRequired);
        Assert.True(policy.Decide(ambiguity).HumanRequired);
    }

    [Fact]
    public void TransientRecoveryUsesBoundedExponentialBackoff()
    {
        var policy = new RecoveryPolicy(new OrchestratorOptions { BackoffInitialSeconds = 15, BackoffMaxSeconds = 60 });
        var first = policy.Decide(Context(FailureCategory.ProviderRateLimit, 0));
        var second = policy.Decide(Context(FailureCategory.ProviderRateLimit, 1));
        Assert.Equal(TimeSpan.FromSeconds(15), first.Delay);
        Assert.Equal(TimeSpan.FromSeconds(30), second.Delay);
        Assert.False(first.HumanRequired);
    }

    [Fact]
    public void ExhaustedBudgetRequiresHuman()
    {
        var policy = new RecoveryPolicy(new OrchestratorOptions { ProviderTransientRetries = 2 });
        Assert.True(policy.Decide(Context(FailureCategory.ProviderRateLimit, 2)).HumanRequired);
    }

    [Fact]
    public void ValidationCategoriesRemainExplicit()
    {
        var failed = TestData.Validation(false) with { Category = "StaticAnalysis" };
        Assert.Equal(FailureCategory.StaticAnalysisFailure, FailureClassifier.Classify(WorkflowState.Validating, validation: [failed]));
        Assert.Equal(FailureCategory.ReviewFailure, FailureClassifier.Classify(WorkflowState.Reviewing, review: TestData.Review(ReviewDecision.Fail, FindingSeverity.High)));
    }

    [Fact]
    public void ProviderAndProtocolBudgetsAreIndependentFromEngineeringBudget()
    {
        var options = new OrchestratorOptions { EngineeringRemediationCycles = 5, EngineeringProgressExtensions = 2, ProviderTransientRetries = 3, MalformedOutputRetries = 2 };
        var provider = Context(FailureCategory.ProviderRateLimit, 3) with { EngineeringRemediationCycles = 5 };
        var protocol = Context(FailureCategory.ProviderMalformedOutput, 2) with { EngineeringRemediationCycles = 5 };
        var review = Context(FailureCategory.ReviewFailure, 2) with { EngineeringRemediationCycles = 2, ProgressObserved = true };

        Assert.True(new RecoveryPolicy(options).Decide(provider).HumanRequired);
        Assert.True(new RecoveryPolicy(options).Decide(protocol).HumanRequired);
        Assert.False(new RecoveryPolicy(options).Decide(review).HumanRequired);
    }

    [Theory]
    [InlineData("force push to protected branch", FailureCategory.ProtectedGitOperation)]
    [InlineData("mutation of the read-only reference monolith", FailureCategory.ReferenceRepositoryMutation)]
    [InlineData("security decision required for authentication boundary", FailureCategory.SecurityDecisionRequired)]
    [InlineData("external authorization credential required", FailureCategory.ExternalAuthorizationRequired)]
    public void ProtectedAndOwnerDecisionsAreHumanRequired(string message, FailureCategory expected)
    {
        var category = FailureClassifier.Classify(WorkflowState.Implementing, message: message);
        Assert.Equal(expected, category);
        Assert.True(new RecoveryPolicy(new OrchestratorOptions()).Decide(Context(category)).HumanRequired);
    }

    [Fact]
    public void ReviewProgressRecognizesChangedAndDecreasingFindings()
    {
        var run = TestData.Run();
        var first = TestData.Review(ReviewDecision.Fail, FindingSeverity.High);
        var second = first with { Findings = [new ReviewFinding(FindingSeverity.Medium, "file", "1", "smaller problem", "impact", "fix")] };
        var third = second with { Findings = [] };

        Assert.True(ReviewConvergence.Observe(run, first).ProgressObserved);
        Assert.True(ReviewConvergence.Observe(run, second).ProgressObserved);
        Assert.True(ReviewConvergence.Observe(run, third).ProgressObserved);
        Assert.Equal(3, run.ReviewProgressHistory.Count);
    }

    [Fact]
    public void ReviewProgressDetectsUnchangedSubstantiveFinding()
    {
        var run = TestData.Run();
        var review = TestData.Review(ReviewDecision.Fail, FindingSeverity.High);
        ReviewConvergence.Observe(run, review);
        var snapshot = ReviewConvergence.Observe(run, review);

        Assert.False(snapshot.ProgressObserved);
        Assert.Equal(2, snapshot.UnchangedOccurrences);
    }

    private static FailureContext Context(FailureCategory category, int attempts = 0) => new(
        "RUN", "TASK", null, WorkflowState.Remediating, WorkflowState.Validating, category, null, 1, "", "", false,
        "anthropic", null, null, null, [], [], [], null, false, null, [], null,
        new Dictionary<string, int> { [category.ToString()] = attempts }, [], "feature/test", [], "", false, RecoveryAction.HumanRequired, false);
}
