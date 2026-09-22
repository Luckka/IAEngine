using System.Text.Json.Serialization;

namespace OnlineOs.AiOrchestrator.Roadmap;

public sealed class RoadmapDocument
{
    public List<MilestoneDefinition> Milestones { get; init; } = [];
}

public sealed class MilestoneDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Branch { get; init; }
    public string? Status { get; init; }
    public List<RoadmapTaskDefinition> Tasks { get; init; } = [];
    public List<string> PrototypeStates { get; init; } = [];
}

public sealed class RoadmapTaskDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public List<string> AcceptanceCriteria { get; init; } = [];
    public List<string> DependsOn { get; init; } = [];
    public List<string> RiskHints { get; init; } = [];
    public List<string> ContextHints { get; init; } = [];
    public List<string> Skills { get; init; } = [];
    public List<string> PrototypeStates { get; init; } = [];
    public bool PrototypeConformityRequired { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MilestoneRuntimeStatus { Running, CompleteAwaitingApproval, Approved, HumanRequired, Failed }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MilestoneTaskStatus { Pending, Ready, Running, Done, Blocked, HumanRequired }

public sealed class MilestoneRuntimeState
{
    public required string MilestoneId { get; init; }
    public MilestoneRuntimeStatus Status { get; set; } = MilestoneRuntimeStatus.Running;
    public Dictionary<string, MilestoneTaskRuntime> Tasks { get; init; } = [];
    public string? CurrentTaskId { get; set; }
    public string? ActiveRunId { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public bool RequiresHumanCheckpoint { get; set; }
    public string? FailureReason { get; set; }
    public string? RequiredAction { get; set; }
}

public sealed class MilestoneTaskRuntime
{
    public MilestoneTaskStatus Status { get; set; } = MilestoneTaskStatus.Pending;
    public string? RunId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public string? RequiredAction { get; set; }
}
