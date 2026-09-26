using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Roadmap;
using IAEngine.Core.Git;
using RoadmapMilestoneDefinition = OnlineOs.AiOrchestrator.Roadmap.MilestoneDefinition;

namespace OnlineOs.AiOrchestrator.Hosting;

public interface IEngineTaskSource
{
    Task<DevelopmentTask> LoadTaskAsync(string taskId, CancellationToken cancellationToken = default);
}

public interface IEngineMilestoneSource
{
    Task<RoadmapMilestoneDefinition> LoadMilestoneAsync(string milestoneId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Consumer-owned request construction. The Engine owns when the request is
/// evaluated; the consumer owns expected files and authorization inputs.
/// </summary>
public interface IGitCheckpointRequestSource
{
    GitCheckpointRequest CreateForTask(RunRecord run);
    GitCheckpointRequest CreateForMilestone(string milestoneId, EngineMilestoneExecutionResult result);
}

public sealed record EngineHostComponentNames(
    string RouterProvider,
    string ImplementationProvider,
    string ReviewProvider,
    string ValidationCapability);

public sealed class EngineHostContext
{
    public required string ProjectId { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required AppOptions Options { get; init; }
    public required IProjectComposition Composition { get; init; }
    public required Action<EngineCompositionRuntimeBuilder> RegisterComponents { get; init; }
    public required EngineHostComponentNames Components { get; init; }
    public required IRunStore RunStore { get; init; }
    public required IGitService Git { get; init; }
    public IProgressReporter? ProgressReporter { get; init; }
    public IEngineReferenceContextProvider? ReferenceContextProvider { get; init; }
    public string? ExpectedBranch { get; init; }
    public IEngineTaskSource? TaskSource { get; init; }
    public IEngineMilestoneSource? MilestoneSource { get; init; }
    public IGitCheckpointCoordinator? CheckpointCoordinator { get; init; }
    public IGitCheckpointRequestSource? CheckpointRequestSource { get; init; }
    public string MilestoneStateDirectory { get; init; } = ".ai-state";
}

public sealed record EngineExecutionResult(
    string ProjectId,
    string RunId,
    WorkflowState State,
    string? FinalDecision,
    bool Succeeded,
    string? FailureReason,
    GitCheckpointExecutionResult? Checkpoint = null);

public sealed record EngineMilestoneExecutionResult(
    string ProjectId,
    string MilestoneId,
    MilestoneRuntimeStatus Status,
    int CompletedTasks,
    int TotalTasks,
    bool Succeeded,
    bool RequiresHumanApproval,
    string? FailureReason,
    string StateDirectory,
    GitCheckpointExecutionResult? Checkpoint = null);
