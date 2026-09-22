using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Abstractions;

public interface IGitWorkflowManager
{
    Task<GitWorkflowResult> PrepareMilestoneAsync(MilestoneDefinition milestone, string? runId = null, CancellationToken cancellationToken = default);
    Task<GitWorkflowResult> FinalizeMilestoneAsync(MilestoneDefinition milestone, bool gatesPassed, string? runId = null, CancellationToken cancellationToken = default);
    Task<GitLifecycleMetadata?> LoadAsync(string milestoneId, CancellationToken cancellationToken = default);
}

public sealed record GitWorkflowResult(bool Succeeded, GitLifecycleState State, string Summary, GitLifecycleMetadata Metadata);
