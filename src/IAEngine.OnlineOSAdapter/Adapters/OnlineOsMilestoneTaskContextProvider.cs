using OnlineOs.AiOrchestrator.Roadmap;

namespace OnlineOs.AiOrchestrator.Adapters;

/// <summary>Preserves the historical Flutter classification for OnlineOS milestones.</summary>
public sealed class OnlineOsMilestoneTaskContextProvider : IMilestoneTaskContextProvider
{
    public IReadOnlyList<string> GetDomains(MilestoneDefinition milestone, RoadmapTaskDefinition task)
        => ["milestone", "flutter"];
}
