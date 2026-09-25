using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;
using OnlineOs.AiOrchestrator.Abstractions;

namespace OnlineOs.AiOrchestrator.Roadmap;

public sealed class MilestoneRunner(
    RoadmapCatalog roadmap,
    RoadmapStateStore stateStore,
    IRunStore runs,
    Orchestrator orchestrator,
    IGitWorkflowManager? gitWorkflow = null,
    TextWriter? output = null,
    IMilestoneTaskGate? taskGate = null,
    IMilestoneTaskContextProvider? taskContextProvider = null)
{
    private TextWriter Output { get; } = output ?? TextWriter.Null;

    public async Task<MilestoneRuntimeState> RunAsync(string milestoneId, CancellationToken ct = default)
    {
        var definition = roadmap.Get(milestoneId);
        if (string.Equals(definition.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            Output.WriteLine($"{milestoneId} already COMPLETE.");
            return new MilestoneRuntimeState { MilestoneId = milestoneId, Status = MilestoneRuntimeStatus.Approved, RequiresHumanCheckpoint = false };
        }
        if (definition.Tasks.Count == 0) throw new InvalidOperationException($"Milestone '{milestoneId}' is a placeholder and is not executable.");
        await EnsureCanStartAsync(definition, ct);
        var state = await LoadOrInitializeAsync(definition, ct);
        if (state.Status == MilestoneRuntimeStatus.CompleteAwaitingApproval || state.Status == MilestoneRuntimeStatus.Approved)
            return state;
        return await DriveAsync(definition, state, null, ct);
    }

    public async Task<MilestoneRuntimeState?> ContinueAsync(RunRecord activeRun, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(activeRun.MilestoneId)) return null;
        var definition = roadmap.Get(activeRun.MilestoneId);
        var state = await stateStore.LoadAsync(ct) ?? throw new InvalidOperationException("Milestone runtime state is missing for the active milestone run.");
        if (state.MilestoneId != definition.Id || state.CurrentTaskId != activeRun.MilestoneTaskId)
            throw new InvalidOperationException("Active run and milestone runtime state do not refer to the same task.");
        var resumed = await orchestrator.ContinueAsync(activeRun, ct);
        if (resumed.State != WorkflowState.HumanRequired && state.Status == MilestoneRuntimeStatus.HumanRequired)
        {
            state.Status = MilestoneRuntimeStatus.Running;
            state.RequiresHumanCheckpoint = false;
            state.FailureReason = null;
            state.RequiredAction = null;
            if (state.CurrentTaskId is not null)
                state.Tasks[state.CurrentTaskId].Status = MilestoneTaskStatus.Running;
            await stateStore.SaveAsync(state, ct);
        }
        return await DriveAsync(definition, state, resumed, ct);
    }

    public async Task<MilestoneRuntimeState> ApproveAsync(string milestoneId, CancellationToken ct = default)
    {
        var state = await stateStore.LoadAsync(ct) ?? throw new InvalidOperationException($"No runtime state exists for milestone '{milestoneId}'.");
        if (state.MilestoneId != milestoneId) throw new InvalidOperationException($"Runtime state belongs to milestone '{state.MilestoneId}'.");
        if (state.Status != MilestoneRuntimeStatus.CompleteAwaitingApproval)
            throw new InvalidOperationException("Only a successfully completed milestone can receive checkpoint approval.");
        state.Status = MilestoneRuntimeStatus.Approved;
        state.RequiresHumanCheckpoint = false;
        await stateStore.SaveAsync(state, ct);
        return state;
    }

    private async Task<MilestoneRuntimeState> DriveAsync(MilestoneDefinition definition, MilestoneRuntimeState state, RunRecord? completedRun, CancellationToken ct)
    {
        if (completedRun is not null)
            await HandleTaskResultAsync(definition, state, completedRun, ct);
        while (state.Status == MilestoneRuntimeStatus.Running)
        {
            var next = roadmap.SelectNextTask(definition, state);
            if (next is null)
            {
                if (state.Tasks.Values.All(task => task.Status == MilestoneTaskStatus.Done))
                {
                    if (gitWorkflow is not null)
                    {
                        var gitMilestone = new OnlineOs.AiOrchestrator.Models.MilestoneDefinition(
                            definition.Id,
                            definition.Title,
                            definition.Branch ?? $"feature/{definition.Id.ToLowerInvariant()}");
                        var finalized = await gitWorkflow.FinalizeMilestoneAsync(gitMilestone, gatesPassed: true, cancellationToken: ct);
                        if (!finalized.Succeeded)
                        {
                            state.Status = finalized.State == GitLifecycleState.HumanRequired
                                ? MilestoneRuntimeStatus.HumanRequired
                                : MilestoneRuntimeStatus.Failed;
                            state.RequiresHumanCheckpoint = false;
                            state.FailureReason = finalized.Summary;
                            state.RequiredAction = finalized.State.ToString();
                            await stateStore.SaveAsync(state, ct);
                            Output.WriteLine($"MILESTONE FINALIZATION BLOCKED: {definition.Id}: {finalized.Summary}");
                            return state;
                        }
                    }
                    state.Status = MilestoneRuntimeStatus.CompleteAwaitingApproval;
                    state.RequiresHumanCheckpoint = true;
                    state.CompletedAt = DateTimeOffset.UtcNow;
                    state.CurrentTaskId = null;
                    state.ActiveRunId = null;
                    await stateStore.SaveAsync(state, ct);
                    Output.WriteLine($"MILESTONE COMPLETE: {definition.Id} ({state.Tasks.Count}/{state.Tasks.Count} DONE). Human checkpoint required.");
                }
                else await stateStore.SaveAsync(state, ct);
                return state;
            }

            var taskState = state.Tasks[next.Id];
            taskState.Status = MilestoneTaskStatus.Running;
            state.CurrentTaskId = next.Id;
            state.ActiveRunId = null;
            await stateStore.SaveAsync(state, ct);
            Output.WriteLine($"Starting {next.Id}: {next.Title}");
            var task = CompileTask(definition, next, state);
            var runId = Orchestrator.CreateRunId();
            taskState.RunId = runId;
            state.ActiveRunId = runId;
            await stateStore.SaveAsync(state, ct);
            var result = await orchestrator.ExecuteAsync(task, false, ct, runId, milestoneId: definition.Id, milestoneTaskId: next.Id);
            await HandleTaskResultAsync(definition, state, result.Run, ct);
        }
        return state;
    }

    private async Task HandleTaskResultAsync(MilestoneDefinition definition, MilestoneRuntimeState state, RunRecord run, CancellationToken ct)
    {
        var taskId = run.MilestoneTaskId ?? state.CurrentTaskId ?? throw new InvalidOperationException("Milestone run has no task association.");
        var taskState = state.Tasks[taskId];
        taskState.RunId = run.RunId;
        state.ActiveRunId = run.State is WorkflowState.Approved or WorkflowState.HumanRequired or WorkflowState.Failed ? null : run.RunId;
        if (run.State == WorkflowState.Approved && string.Equals(run.FinalDecision, "PASS", StringComparison.OrdinalIgnoreCase))
        {
            var taskDefinition = definition.Tasks.Single(task => task.Id == taskId);
            if (taskGate is not null)
            {
                var gate = await taskGate.EvaluateAsync(stateStore.RepositoryRoot, definition, taskDefinition, ct);
                if (!gate.Passed)
                {
                    taskState.Status = MilestoneTaskStatus.Blocked;
                    taskState.FailureReason = gate.Code;
                    taskState.RequiredAction = string.Join(", ", gate.Missing);
                    state.Status = MilestoneRuntimeStatus.Failed;
                    state.FailureReason = gate.Code;
                    state.RequiredAction = taskState.RequiredAction;
                    await stateStore.SaveAsync(state, ct);
                    Output.WriteLine($"MILESTONE TASK GATE BLOCKED {taskId}: {taskState.RequiredAction}");
                    return;
                }
            }
            taskState.Status = MilestoneTaskStatus.Done;
            taskState.CompletedAt = DateTimeOffset.UtcNow;
            taskState.FailureReason = null;
            state.CurrentTaskId = null;
            state.ActiveRunId = null;
            await stateStore.SaveAsync(state, ct);
            Output.WriteLine($"DONE {taskId}");
            return;
        }
        if (run.State == WorkflowState.HumanRequired)
        {
            taskState.Status = MilestoneTaskStatus.HumanRequired;
            taskState.FailureReason = run.LastFailure?.RootCause;
            taskState.RequiredAction = run.LastFailure?.RecommendedAction.ToString();
            state.Status = MilestoneRuntimeStatus.HumanRequired;
            state.FailureReason = taskState.FailureReason;
            state.RequiredAction = taskState.RequiredAction;
        }
        else if (run.State == WorkflowState.Failed)
        {
            taskState.Status = MilestoneTaskStatus.Blocked;
            taskState.FailureReason = run.LastFailure?.RootCause ?? run.Transitions.LastOrDefault()?.Reason;
            state.Status = MilestoneRuntimeStatus.Failed;
            state.FailureReason = taskState.FailureReason;
        }
        await stateStore.SaveAsync(state, ct);
    }

    private async Task<MilestoneRuntimeState> LoadOrInitializeAsync(MilestoneDefinition definition, CancellationToken ct)
    {
        var state = await stateStore.LoadAsync(ct);
        if (state is not null && state.MilestoneId != definition.Id)
            throw new InvalidOperationException($"Another milestone runtime state is active: {state.MilestoneId}.");
        if (state is not null) return state;
        state = new MilestoneRuntimeState { MilestoneId = definition.Id };
        foreach (var task in definition.Tasks) state.Tasks[task.Id] = new MilestoneTaskRuntime();
        await stateStore.SaveAsync(state, ct);
        return state;
    }

    private async Task EnsureCanStartAsync(MilestoneDefinition definition, CancellationToken ct)
    {
        var activeHealth = await runs.AssessActiveAsync(ct);
        if (activeHealth.Health is RunHealth.ValidActive or RunHealth.WaitingRetry or RunHealth.HumanRequired or RunHealth.Orphaned)
            throw new InvalidOperationException($"An active run blocks milestone start: {activeHealth.Reason}. {activeHealth.NextAction ?? "Use status or the safe run abandon command."}");
        var existing = await stateStore.LoadAsync(ct);
        if (existing?.MilestoneId == definition.Id && existing.Status is MilestoneRuntimeStatus.Running or MilestoneRuntimeStatus.HumanRequired or MilestoneRuntimeStatus.Failed)
            throw new InvalidOperationException($"Milestone {definition.Id} already has runtime state {existing.Status}. Use status or continue; it was not restarted.");
        var prior = roadmap.Document.Milestones.TakeWhile(x => x.Id != definition.Id).LastOrDefault();
        if (prior is null || string.Equals(prior.Status, "completed", StringComparison.OrdinalIgnoreCase)) return;
        var state = existing;
        if (state is null || state.MilestoneId != prior.Id || state.Status != MilestoneRuntimeStatus.Approved)
            throw new InvalidOperationException($"Milestone {definition.Id} is blocked until checkpoint approval for {prior.Id}.");
    }

    private DevelopmentTask CompileTask(MilestoneDefinition milestone, RoadmapTaskDefinition task, MilestoneRuntimeState state)
    {
        var completed = milestone.Tasks.Where(x => state.Tasks.TryGetValue(x.Id, out var runtime) && runtime.Status == MilestoneTaskStatus.Done).Select(x => $"{x.Id}: DONE").ToArray();
        var description = $"Milestone {milestone.Id} — {milestone.Title}. Task {task.Id} — {task.Title}.\n{task.Description}\n\nScope boundaries: implement only confirmed requirements; do not invent API or backend rules; do not implement future milestone scope.\nCompleted dependencies: {(completed.Length == 0 ? "none" : string.Join(", ", completed))}";
        var domains = taskContextProvider?.GetDomains(milestone, task) ?? ["milestone"];
        return new DevelopmentTask(task.Id, task.Title, description, Domains: domains, Risk: task.RiskHints.FirstOrDefault(), Skills: task.Skills, RelevantContext: task.ContextHints, AcceptanceCriteria: task.AcceptanceCriteria);
    }
}
