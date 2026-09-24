using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Roadmap;

/// <summary>
/// Optional consumer-owned gate evaluated after a task reaches the generic
/// Approved state. Technology-specific evidence stays outside the Core.
/// </summary>
public interface IMilestoneTaskGate
{
    Task<MilestoneGateResult> EvaluateAsync(
        string repositoryRoot,
        MilestoneDefinition milestone,
        RoadmapTaskDefinition task,
        CancellationToken cancellationToken = default);
}

public sealed record MilestoneGateResult(bool Passed, string Code, IReadOnlyList<string> Missing);
