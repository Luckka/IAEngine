using IAEngine.Core.Git;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Hosting;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;

namespace IAEngine.Core.Tests;

public sealed class GitCheckpointPolicyTests
{
    [Fact]
    public void AllowsCheckpointAfterApprovedTask()
    {
        var decision = GitCheckpointPolicy.Evaluate(ValidRequest());

        Assert.Equal(GitCheckpointDecisionStatus.Allowed, decision.Status);
        Assert.True(decision.CommitAllowed);
    }

    [Fact]
    public void BlocksValidationFailure()
        => AssertBlocked(ValidRequest() with { ValidationPassed = false }, "validation-failed");

    [Fact]
    public void BlocksReviewFailure()
        => AssertBlocked(ValidRequest() with { ReviewPassed = false }, "review-failed");

    [Fact]
    public void HumanRequiredBlocksCommit()
    {
        var decision = GitCheckpointPolicy.Evaluate(ValidRequest() with { State = GitCheckpointState.HumanRequired });

        Assert.Equal(GitCheckpointDecisionStatus.HumanRequired, decision.Status);
        Assert.False(decision.CommitAllowed);
        Assert.True(decision.RequiresHumanApproval);
    }

    [Fact]
    public void BlocksWithoutChanges()
        => AssertBlocked(ValidRequest() with { HasChanges = false, ChangedFiles = [] }, "no-changes");

    [Fact]
    public void BlocksInconsistentDiff()
        => AssertBlocked(ValidRequest() with { DiffCheckPassed = false }, "diff-check-failed");

    [Fact]
    public void BlocksUnauthorizedBranch()
        => AssertBlocked(ValidRequest() with { BranchAuthorized = false }, "branch-not-authorized");

    [Fact]
    public void BlocksInvalidSemanticMessage()
        => AssertBlocked(ValidRequest() with { ProposedCommitMessage = "checkpoint changes" }, "invalid-semantic-message");

    [Fact]
    public void BlocksUnrelatedChanges()
        => AssertBlocked(ValidRequest() with { ChangedFiles = ["src/allowed.cs", "secret.txt"] }, "unrelated-changes");

    [Fact]
    public void AllowsCommitAfterExplicitHumanApproval()
    {
        var decision = GitCheckpointPolicy.Evaluate(ValidRequest() with
        {
            HumanApprovalRequired = true,
            HumanApprovalProvided = true
        });

        Assert.Equal(GitCheckpointDecisionStatus.Allowed, decision.Status);
    }

    [Fact]
    public void BlocksIncompleteMilestone()
        => AssertBlocked(ValidRequest() with
        {
            Scope = GitCheckpointScope.Milestone,
            State = GitCheckpointState.Approved,
            MilestoneCompleted = false
        }, "milestone-incomplete");

    [Fact]
    public void BlocksAwaitingMilestoneApproval()
        => AssertBlocked(ValidRequest() with
        {
            Scope = GitCheckpointScope.Milestone,
            State = GitCheckpointState.CompleteAwaitingApproval,
            MilestoneCompleted = true
        }, "milestone-awaiting-approval");

    [Fact]
    public void PolicyIsDeterministic()
    {
        var first = GitCheckpointPolicy.Evaluate(ValidRequest());
        var second = GitCheckpointPolicy.Evaluate(ValidRequest());

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Reason, second.Reason);
        Assert.Equal(first.Evidence, second.Evidence);
        Assert.Equal(first.FilesEvaluated, second.FilesEvaluated);
        Assert.Equal(first.CommitAllowed, second.CommitAllowed);
    }

    [Fact]
    public async Task EngineHostRequestsAndPersistsAllowedCheckpoint()
    {
        using var workspace = new TemporaryWorkspace();
        var coordinator = new RecordingCoordinator();
        var source = new RecordingRequestSource();
        var host = CreateHost(workspace.Path, coordinator, source);

        var result = await host.RunAsync(new DevelopmentTask("CHECKPOINT-001", "Checkpoint task", "Execute a local checkpoint."));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Checkpoint);
        Assert.Equal(GitCheckpointDecisionStatus.Allowed, result.Checkpoint!.Decision.Status);
        Assert.True(result.Checkpoint.Result!.Succeeded);
        Assert.Equal("abc123", result.Checkpoint.Result.CommitSha);
        Assert.Single(coordinator.EvaluatedRequests);
        Assert.Single(coordinator.CommitRequests);
        var run = await new RunStore(workspace.Path, ".ai-runs").LoadAsync(result.RunId);
        Assert.NotNull(run!.GitCheckpointDecision);
        Assert.Equal("abc123", run.GitCheckpointResult!.CommitSha);
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".ai-runs", result.RunId, "git-checkpoint-result.json")));
    }

    [Fact]
    public async Task MissingCoordinatorDoesNotBreakGenericExecutionOrCommit()
    {
        using var workspace = new TemporaryWorkspace();
        var source = new RecordingRequestSource();
        var host = CreateHost(workspace.Path, coordinator: null, source);

        var result = await host.RunAsync(new DevelopmentTask("CHECKPOINT-002", "Generic task", "Execute without checkpoint coordinator."));

        Assert.True(result.Succeeded);
        Assert.Null(result.Checkpoint);
        Assert.Empty(source.CreatedRequests);
    }

    [Fact]
    public async Task CoordinatorWithoutRequestSourceCannotCommitAutomatically()
    {
        using var workspace = new TemporaryWorkspace();
        var coordinator = new RecordingCoordinator();
        var host = CreateHost(workspace.Path, coordinator, source: null);

        var result = await host.RunAsync(new DevelopmentTask("CHECKPOINT-003", "Generic task", "No request source."));

        Assert.True(result.Succeeded);
        Assert.Null(result.Checkpoint);
        Assert.Empty(coordinator.CommitRequests);
    }

    [Fact]
    public async Task PushAndMergeResultsAreRejectedByHost()
    {
        using var workspace = new TemporaryWorkspace();
        var coordinator = new RecordingCoordinator
        {
            CommitResult = ValidCommit() with { PushPerformed = true, MergePerformed = true }
        };
        var host = CreateHost(workspace.Path, coordinator, new RecordingRequestSource());

        var result = await host.RunAsync(new DevelopmentTask("CHECKPOINT-004", "Unsafe result", "Reject forbidden Git operations."));

        Assert.NotNull(result.Checkpoint);
        Assert.False(result.Checkpoint!.Result!.Succeeded);
        Assert.Contains("push and merge", result.Checkpoint.Result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContractDoesNotReferenceLegacyOrConsumerTechnologies()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "IAEngine.Core", "Git", "GitCheckpointContracts.cs"));

        foreach (var token in new[] { "InfraSentinel", "OnlineOS", "Flutter", "Patrol", "ADB", "AWS", "developer", "producao" })
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);

        var references = typeof(IGitCheckpointCoordinator).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => string.Equals(reference.Name, "IAEngine.OnlineOSAdapter", StringComparison.Ordinal));
    }

    private static GitCheckpointRequest ValidRequest(string? workspace = null)
    {
        workspace ??= RepositoryRoot();
        return new(
            "generic-project",
            workspace,
            GitCheckpointScope.Task,
            null,
            "TASK-001",
            "feature/checkpoint",
            "feat: add generic checkpoint",
            GitCheckpointState.Approved,
            true,
            true,
            true,
            true,
            false,
            false,
            false,
            true,
            true,
            ["src/allowed.cs"],
            ["src/allowed.cs"],
            true,
            true,
            false,
            false);
    }

    private static void AssertBlocked(GitCheckpointRequest request, string evidence)
    {
        var decision = GitCheckpointPolicy.Evaluate(request);
        Assert.Equal(GitCheckpointDecisionStatus.Blocked, decision.Status);
        Assert.False(decision.CommitAllowed);
        Assert.Contains(decision.Evidence, item => item.Contains(evidence, StringComparison.OrdinalIgnoreCase));
    }

    private static EngineHost CreateHost(string workspace, RecordingCoordinator? coordinator, RecordingRequestSource? source)
    {
        var options = new AppOptions
        {
            Project = new ProjectProfileOptions { Id = "generic-project", WorkspaceRoot = workspace, Stack = "generic" },
            Orchestrator = new OrchestratorOptions { RunsDirectory = ".ai-runs", ProcessTimeoutSeconds = 30 },
            ReviewPolicy = new ReviewPolicyOptions()
        };
        var composition = new EngineCompositionPlan("generic-project", false, false, false, false, false, [],
            ["router", "implementation", "review"], [], ["policy"], ["validation"]);
        return EngineHost.Create(new EngineHostContext
        {
            ProjectId = "generic-project",
            WorkspaceRoot = workspace,
            Options = options,
            Composition = composition,
            Components = new("router", "implementation", "review", "validation"),
            RegisterComponents = builder => builder
                .RegisterProvider<ITaskRouter>("router", () => new LocalRouter())
                .RegisterProvider<IImplementationAgent>("implementation", () => new LocalImplementation())
                .RegisterProvider<IReviewAgent>("review", () => new LocalReview())
                .RegisterPolicy("policy", () => new LocalPolicy())
                .RegisterCapability<IValidationRunner>("validation", () => new LocalValidation()),
            RunStore = new RunStore(workspace, ".ai-runs"),
            Git = new LocalGit(workspace),
            CheckpointCoordinator = coordinator,
            CheckpointRequestSource = source
        });
    }

    private static GitCheckpointResult ValidCommit() => new(true, "abc123", "feat: add generic checkpoint", "feature/checkpoint", ["src/allowed.cs"], DateTimeOffset.UtcNow, null, true, false, false);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private sealed class RecordingCoordinator : IGitCheckpointCoordinator
    {
        public List<GitCheckpointRequest> EvaluatedRequests { get; } = [];
        public List<GitCheckpointRequest> CommitRequests { get; } = [];
        public GitCheckpointResult CommitResult { get; init; } = ValidCommit();

        public Task<GitCheckpointDecision> EvaluateAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
        {
            EvaluatedRequests.Add(request);
            return Task.FromResult(new GitCheckpointDecision(GitCheckpointDecisionStatus.Allowed, "fake allowed", ["fake"], request.ChangedFiles, request.CurrentBranch, request.ProposedCommitMessage, true, false));
        }

        public Task<GitCheckpointResult> CommitAsync(GitCheckpointRequest request, CancellationToken cancellationToken = default)
        {
            CommitRequests.Add(request);
            return Task.FromResult(CommitResult);
        }
    }

    private sealed class RecordingRequestSource : IGitCheckpointRequestSource
    {
        public List<GitCheckpointRequest> CreatedRequests { get; } = [];

        public GitCheckpointRequest CreateForTask(RunRecord run)
        {
            var request = ValidRequest() with { TaskId = run.Task.Id, ChangedFiles = run.ChangedFiles, ExpectedFiles = run.ChangedFiles };
            CreatedRequests.Add(request);
            return request;
        }

        public GitCheckpointRequest CreateForMilestone(string milestoneId, EngineMilestoneExecutionResult result)
            => ValidRequest() with { Scope = GitCheckpointScope.Milestone, MilestoneId = milestoneId, MilestoneCompleted = result.Status == OnlineOs.AiOrchestrator.Roadmap.MilestoneRuntimeStatus.Approved };
    }

    private sealed class LocalRouter : ITaskRouter
    {
        public Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken cancellationToken = default)
            => Task.FromResult(new RoutingResult("generic", ["generic"], "low", "low", [], [], "local", "fake", true));

        public Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((true, true, "fake"));
    }

    private sealed class LocalImplementation : IImplementationAgent
    {
        public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImplementationResult(true, "fake", ["src/allowed.cs"]));

        public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken cancellationToken = default, int contextReductionAttempt = 0)
            => Task.FromResult(new ImplementationResult(true, "fake remediation", ["src/allowed.cs"]));
    }

    private sealed class LocalReview : IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReviewResult(ReviewDecision.Pass, [], "fake"));
    }

    private sealed class LocalValidation : IValidationRunner
    {
        public Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ValidationResult>>([new("generic", true, new ProcessResult("fake", 0, "", "", TimeSpan.Zero))]);
    }

    private sealed record LocalPolicy(string Name = "policy") : IProjectPolicyComponent;

    private sealed class LocalGit(string root) : IGitService
    {
        public Task<string> GetRootAsync(CancellationToken cancellationToken = default) => Task.FromResult(root);
        public Task<string> GetBranchAsync(CancellationToken cancellationToken = default) => Task.FromResult("feature/checkpoint");
        public Task<string> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> GetDiffAsync(CancellationToken cancellationToken = default) => Task.FromResult("diff --git a/src/allowed.cs b/src/allowed.cs");
        public Task<string> GetCommitAsync(CancellationToken cancellationToken = default) => Task.FromResult("base");
        public Task<(bool Safe, string Reason)> CheckBranchSafetyAsync(CancellationToken cancellationToken = default) => Task.FromResult((true, "fake"));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("iaengine-checkpoint-");
        public string Path => directory.FullName;
        public void Dispose() => directory.Delete(true);
    }
}
