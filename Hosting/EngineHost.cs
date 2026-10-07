using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;
using OnlineOs.AiOrchestrator.Roadmap;
using IAEngine.Core.Git;
using IAEngine.Core.Memory;
using IAEngine.Core.Recovery;
using RoadmapMilestoneDefinition = OnlineOs.AiOrchestrator.Roadmap.MilestoneDefinition;

namespace OnlineOs.AiOrchestrator.Hosting;

/// <summary>
/// Generic execution host. The consumer owns configuration, component factories,
/// workspace services and persistence; the Engine owns runtime resolution and the
/// existing workflow executor.
/// </summary>
public sealed class EngineHost
{
    private readonly EngineHostContext context;
    private readonly EngineCompositionRuntime runtime;
    private readonly Orchestrator orchestrator;

    private EngineHost(EngineHostContext context, EngineCompositionRuntime runtime, Orchestrator orchestrator)
    {
        this.context = context;
        this.runtime = runtime;
        this.orchestrator = orchestrator;
    }

    public string ProjectId => context.ProjectId;
    public string WorkspaceRoot => context.WorkspaceRoot;
    public EngineCompositionRuntime Runtime => runtime;
    public IMemoryEventSink? MemoryEventSink => context.MemoryEventSink;
    public bool RecoveryConfigured => context.RecoveryService is not null;

    public Task<RecoveryExecution> StartRecoveryAsync(
        ExecutionKey key,
        CancellationToken cancellationToken = default)
        => RequireRecovery().StartAsync(key, cancellationToken);

    public Task<RecoveryExecution?> GetRecoveryAsync(
        ExecutionKey key,
        CancellationToken cancellationToken = default)
        => RequireRecovery().GetAsync(key, cancellationToken);

    public async Task<IReadOnlyList<RecoveryAttempt>> GetRecoveryAttemptsAsync(
        ExecutionKey key,
        CancellationToken cancellationToken = default)
    {
        var execution = await RequireRecovery().GetAsync(key, cancellationToken);
        return execution?.Attempts ?? [];
    }

    public Task<RecoveryAttempt> StartRecoveryAttemptAsync(
        ExecutionKey key,
        RecoveryReason reason,
        CancellationToken cancellationToken = default)
        => RequireRecovery().StartAttemptAsync(key, reason, cancellationToken);

    public Task<RecoveryExecution> RecordRecoveryAttemptAsync(
        ExecutionKey key,
        RecoveryAttempt attempt,
        CancellationToken cancellationToken = default)
        => RequireRecovery().RecordAttemptAsync(key, attempt, cancellationToken);

    public Task<RecoveryExecution> RecoverAsync(
        ExecutionKey key,
        RecoveryReason reason,
        CancellationToken cancellationToken = default)
        => RequireRecovery().RecoverAsync(key, reason, cancellationToken);

    public async Task<EngineMilestoneExecutionResult> RecoverMilestoneAsync(
        ExecutionKey key,
        RecoveryReason reason,
        CancellationToken cancellationToken = default)
    {
        var recovery = await RequireRecovery().GetAsync(key, cancellationToken)
            ?? throw new InvalidOperationException($"Recovery execution '{key}' was not found.");
        if (recovery.Status == RecoveryStatus.HumanRequired && recovery.Approvals.Count == 0)
            throw new InvalidOperationException("Human approval is required before milestone recovery.");
        await RequireRecovery().RecoverAsync(key, reason, cancellationToken);
        var persistedRun = await context.RunStore.LoadAsync(key.ExecutionId, cancellationToken)
            ?? throw new InvalidOperationException($"Persisted run '{key.ExecutionId}' was not found.");
        if (context.MilestoneSource is null)
            throw new InvalidOperationException("No milestone source was supplied by the consumer.");
        var definition = await context.MilestoneSource.LoadMilestoneAsync(key.MilestoneId, cancellationToken);
        var runner = CreateMilestoneRunner(definition);
        var state = await runner.RecoverAsync(persistedRun, $"Generic recovery: {reason}.", cancellationToken)
            ?? throw new InvalidOperationException("Milestone recovery returned no state.");
        return ToMilestoneResult(state);
    }

    public Task<RecoveryExecution> ApproveRecoveryAsync(
        ExecutionKey key,
        string approvalId,
        string reason,
        CancellationToken cancellationToken = default)
        => RequireRecovery().ApproveHumanRequiredAsync(key, approvalId, reason, cancellationToken);

    public Task<RecoveryExecution> RecordRecoveryArtifactAsync(
        ExecutionKey key,
        RecoveryArtifactIdentity artifact,
        CancellationToken cancellationToken = default)
        => RequireRecovery().RecordArtifactAsync(key, artifact, cancellationToken);

    public Task<RecoveryExecution> RecordRecoveryCheckpointAsync(
        ExecutionKey key,
        RecoveryCheckpointIdentity checkpoint,
        CancellationToken cancellationToken = default)
        => RequireRecovery().RecordCheckpointAsync(key, checkpoint, cancellationToken);

    public static EngineHost Create(EngineHostContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateContext(context);

        var builder = context.Composition.CreateRuntimeBuilder();
        context.RegisterComponents(builder);
        var resolved = builder.Build();
        var names = context.Components;

        var router = resolved.ResolveProvider<ITaskRouter>(names.RouterProvider);
        var implementation = resolved.ResolveProvider<IImplementationAgent>(names.ImplementationProvider);
        var reviewer = resolved.ResolveProvider<IReviewAgent>(names.ReviewProvider);
        var validation = resolved.ResolveCapability<IValidationRunner>(names.ValidationCapability);

        var hostOrchestrator = new Orchestrator(
            router,
            implementation,
            validation,
            reviewer,
            context.Git,
            context.RunStore,
            new ReviewPolicy(context.Options.ReviewPolicy),
            new WorkflowStateMachine(),
            context.Options,
            context.ProgressReporter,
            router as IFailureDiagnoser,
            context.ReferenceContextProvider,
            context.ExpectedBranch);

        return new EngineHost(context, resolved, hostOrchestrator);
    }

    public Task<EngineExecutionResult> RunAsync(
        DevelopmentTask task,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(task, dryRun, cancellationToken);

    public async Task<EngineExecutionResult> RunTaskAsync(
        string taskId,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (context.TaskSource is null)
            throw new InvalidOperationException("No task source was supplied by the consumer.");
        return await ExecuteAsync(await context.TaskSource.LoadTaskAsync(taskId, cancellationToken), dryRun, cancellationToken);
    }

    public Task<RoadmapMilestoneDefinition> LoadMilestoneAsync(
        string milestoneId,
        CancellationToken cancellationToken = default)
    {
        if (context.MilestoneSource is null)
            throw new InvalidOperationException("No milestone source was supplied by the consumer.");
        return context.MilestoneSource.LoadMilestoneAsync(milestoneId, cancellationToken);
    }

    public async Task<EngineMilestoneExecutionResult> RunMilestoneAsync(
        string milestoneId,
        CancellationToken cancellationToken = default)
    {
        var definition = await LoadMilestoneAsync(milestoneId, cancellationToken);
        var runner = CreateMilestoneRunner(definition);
        return ToMilestoneResult(await runner.RunAsync(milestoneId, cancellationToken));
    }

    public async Task<EngineMilestoneExecutionResult> ContinueMilestoneAsync(
        CancellationToken cancellationToken = default)
    {
        var activeRun = await context.RunStore.FindActiveAsync(cancellationToken)
            ?? throw new InvalidOperationException("No active Engine run exists for milestone continuation.");
        if (string.IsNullOrWhiteSpace(activeRun.MilestoneId))
            throw new InvalidOperationException("The active Engine run is not associated with a milestone.");
        var definition = await LoadMilestoneAsync(activeRun.MilestoneId, cancellationToken);
        var runner = CreateMilestoneRunner(definition);
        var state = await runner.ContinueAsync(activeRun, cancellationToken)
            ?? throw new InvalidOperationException("Milestone continuation returned no state.");
        return ToMilestoneResult(state);
    }

    public async Task<EngineMilestoneExecutionResult> ApproveMilestoneAsync(
        string milestoneId,
        CancellationToken cancellationToken = default)
    {
        var definition = await LoadMilestoneAsync(milestoneId, cancellationToken);
        var runner = CreateMilestoneRunner(definition);
        var result = ToMilestoneResult(await runner.ApproveAsync(milestoneId, cancellationToken));
        if (context.CheckpointCoordinator is null || context.CheckpointRequestSource is null)
            return result;

        var request = context.CheckpointRequestSource.CreateForMilestone(milestoneId, result);
        return result with { Checkpoint = await RequestCheckpointAsync(request, cancellationToken) };
    }

    public async Task<GitCheckpointExecutionResult> RequestCheckpointAsync(
        GitCheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (context.CheckpointCoordinator is null)
        {
            var blocked = GitCheckpointPolicy.Evaluate(request) with
            {
                Status = GitCheckpointDecisionStatus.Blocked,
                Reason = "No checkpoint coordinator was supplied; automatic commit is disabled.",
                CommitAllowed = false
            };
            return new(blocked, GitCheckpointResult.Blocked(blocked.Reason, request));
        }

        var baseDecision = GitCheckpointPolicy.Evaluate(request);
        if (baseDecision.Status != GitCheckpointDecisionStatus.Allowed)
            return new(baseDecision, GitCheckpointResult.Blocked(baseDecision.Reason, request));

        GitCheckpointDecision decision;
        try
        {
            decision = await context.CheckpointCoordinator.EvaluateAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            decision = GitCheckpointDecision.Invalid($"Checkpoint coordinator evaluation failed: {exception.Message}", request, "coordinator-exception");
        }

        if (decision.Status != GitCheckpointDecisionStatus.Allowed || !decision.CommitAllowed)
            return new(decision, GitCheckpointResult.Blocked(decision.Reason, request));

        GitCheckpointResult result;
        try
        {
            result = await context.CheckpointCoordinator.CommitAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = GitCheckpointResult.Blocked($"Checkpoint commit failed: {exception.Message}", request);
        }

        if (result.PushPerformed || result.MergePerformed)
        {
            result = result with
            {
                Succeeded = false,
                FailureReason = "Checkpoint contract forbids automatic push and merge operations."
            };
        }
        else if (result.Succeeded && string.IsNullOrWhiteSpace(result.CommitSha))
        {
            result = result with
            {
                Succeeded = false,
                FailureReason = "A successful checkpoint must return a commit SHA."
            };
        }

        return new(decision, result);
    }

    private async Task<EngineExecutionResult> ExecuteAsync(DevelopmentTask task, bool dryRun, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);
        await AppendMemoryEventAsync(new MemoryEvent(Guid.NewGuid().ToString("N"), MemoryEventType.TaskStarted, System.Text.Json.JsonSerializer.Serialize(new { summary = task.Title, status = MemoryStatus.Active.ToString() }), context.ProjectId, $"pending-{task.Id}", null, task.Id, DateTimeOffset.UtcNow, task.Id, null), ct);
        var result = await orchestrator.ExecuteAsync(task, dryRun, ct);
        var run = result.Run;
        var taskSucceeded = (run.State is WorkflowState.Approved or WorkflowState.Completed)
            && run.FinalDecision is "PASS" or "DRY_RUN" or "REFERENCE_VALIDATED";
        await AppendMemoryEventAsync(new MemoryEvent(Guid.NewGuid().ToString("N"), taskSucceeded ? MemoryEventType.TaskCompleted : MemoryEventType.TaskFailed, System.Text.Json.JsonSerializer.Serialize(new { summary = task.Title, status = taskSucceeded ? MemoryStatus.Approved.ToString() : MemoryStatus.Unknown.ToString() }), context.ProjectId, run.RunId, run.MilestoneId, run.MilestoneTaskId ?? task.Id, DateTimeOffset.UtcNow, run.RunId, null), ct);
        GitCheckpointExecutionResult? checkpoint = null;
        if (run.State == WorkflowState.Approved && context.CheckpointCoordinator is not null && context.CheckpointRequestSource is not null)
        {
            var request = context.CheckpointRequestSource.CreateForTask(run);
            checkpoint = await RequestCheckpointAsync(request, ct);
            run.GitCheckpointDecision = checkpoint.Decision;
            run.GitCheckpointResult = checkpoint.Result;
            await context.RunStore.SaveArtifactAsync(run.RunId, "git-checkpoint-decision.json", checkpoint.Decision, ct);
            if (checkpoint.Result is not null)
                await context.RunStore.SaveArtifactAsync(run.RunId, "git-checkpoint-result.json", checkpoint.Result, ct);
            await context.RunStore.SaveAsync(run, ct);
        }
        var succeeded = run.State is WorkflowState.Approved or WorkflowState.Completed
            && (run.FinalDecision is "PASS" or "DRY_RUN" or "REFERENCE_VALIDATED");
        return new EngineExecutionResult(
            context.ProjectId,
            run.RunId,
            run.State,
            run.FinalDecision,
            succeeded,
            run.LastFailure?.RootCause,
            checkpoint);
    }

    private async Task AppendMemoryEventAsync(MemoryEvent memoryEvent, CancellationToken ct)
    {
        if (context.MemoryEventSink is not null)
            await context.MemoryEventSink.AppendAsync(memoryEvent, ct);
    }

    private MilestoneRunner CreateMilestoneRunner(RoadmapMilestoneDefinition definition)
        => new(
            RoadmapCatalog.FromMilestone(definition),
            new RoadmapStateStore(context.WorkspaceRoot, context.MilestoneStateDirectory),
            context.RunStore,
            orchestrator,
            output: TextWriter.Null);

    private EngineMilestoneExecutionResult ToMilestoneResult(MilestoneRuntimeState state)
    {
        var completed = state.Tasks.Values.Count(task => task.Status == MilestoneTaskStatus.Done);
        var succeeded = state.Status is MilestoneRuntimeStatus.CompleteAwaitingApproval or MilestoneRuntimeStatus.Approved;
        var requiresHumanApproval = state.RequiresHumanCheckpoint || state.Status == MilestoneRuntimeStatus.HumanRequired;
        return new EngineMilestoneExecutionResult(
            context.ProjectId,
            state.MilestoneId,
            state.Status,
            completed,
            state.Tasks.Count,
            succeeded,
            requiresHumanApproval,
            state.FailureReason,
            context.MilestoneStateDirectory);
    }

    private static void ValidateContext(EngineHostContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ProjectId))
            throw new ArgumentException("Project id is required.", nameof(context));
        if (string.IsNullOrWhiteSpace(context.WorkspaceRoot) || !Directory.Exists(context.WorkspaceRoot))
            throw new ArgumentException("Workspace root must exist.", nameof(context));
        if (!string.Equals(context.ProjectId, context.Composition.ProjectId, StringComparison.Ordinal))
            throw new InvalidOperationException("Host project id must match the declared composition project id.");
        if (!string.Equals(context.ProjectId, context.Options.Project.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Host project id must match the project profile id.");

        var errors = ProjectProfileValidator.Validate(context.Options.Project);
        if (errors.Count > 0)
            throw new CompositionResolutionException(
                $"Project '{context.ProjectId}' configuration is invalid: {string.Join(" ", errors)}", []);
    }

    private IExecutionRecoveryService RequireRecovery()
        => context.RecoveryService ?? throw new InvalidOperationException("Recovery is not configured for this EngineHost.");
}
