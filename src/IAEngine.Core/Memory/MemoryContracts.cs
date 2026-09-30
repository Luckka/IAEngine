using System.Collections.ObjectModel;

namespace IAEngine.Core.Memory;

public enum MemoryType { Working, Episodic, Semantic, Procedural }
public enum MemoryEventType { UserRequest, AgentResponse, TaskCreated, TaskStarted, TaskCompleted, TaskFailed, ValidationPassed, ValidationFailed, ReviewCompleted, RemediationExecuted, ArchitectureDecision, ApprovalGranted, HumanRequired, MilestoneStarted, MilestoneCompleted, CommitCreated, CheckpointCreated, ProviderFailure, RunStatusChanged, MemoryCurated }
public enum MemoryStatus { Active, Superseded, Deprecated, Rejected, Proposed, Approved, Unknown }

public sealed record MemoryEvent(
    string EventId, MemoryEventType Type, string PayloadJson, string ProjectId, string RunId,
    string? MilestoneId, string? TaskId, DateTimeOffset CreatedAt, string CorrelationId,
    string? CausationId, int SchemaVersion = 1, IReadOnlyDictionary<string, string>? Tags = null);

public sealed record MemoryRecord(
    string MemoryId, string ProjectId, string? RunId, string? MilestoneId, string? TaskId,
    string SourceEventId, MemoryType MemoryType, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    MemoryStatus Status, double Confidence, string Summary, string Evidence,
    string? Supersedes = null, string? SupersededBy = null,
    IReadOnlyDictionary<string, string>? Tags = null);

public sealed record MemoryCitation(string CitationId, string SourceEventId, string? Artifact, string Label, string Excerpt);
public sealed record MemoryConflict(string ConflictId, IReadOnlyList<string> MemoryIds, string Description);

public sealed record MemoryQuery(
    string? Text = null, string? ProjectId = null, string? MilestoneId = null, string? TaskId = null,
    MemoryType? MemoryType = null, MemoryStatus? Status = null, DateTimeOffset? From = null,
    DateTimeOffset? To = null, IReadOnlyCollection<string>? Tags = null, string? SourceEventId = null,
    string? CheckpointId = null, int Limit = 50);

public sealed record MemorySearchResult(
    MemoryRecord Memory, double Score, IReadOnlyList<MemoryCitation> Citations,
    MemoryEvent? SourceEvent, IReadOnlyList<string> Limitations, IReadOnlyList<MemoryConflict> Conflicts);

public sealed record MemoryCheckpoint(
    string CheckpointId, string ProjectId, string? RunId, string? MilestoneId,
    string SourceEventId, DateTimeOffset CreatedAt, string Label, int SchemaVersion = 1);

public sealed record MemoryProjection(string ProjectionId, string ProjectId, string Name, DateTimeOffset RebuiltAt, int SourceEventCount, int SchemaVersion = 1);

public sealed record MemoryContext(
    IReadOnlyList<MemorySearchResult> RelevantMemories, IReadOnlyList<MemoryCitation> Evidence,
    IReadOnlyList<MemoryConflict> Conflicts, IReadOnlyList<string> Limitations,
    string CurrentProjectState, string CurrentMilestoneState);

public sealed record RedactionResult(bool Persistable, string SanitizedPayload, IReadOnlyList<string> Limitations);

public interface IMemoryEventSink { Task<MemoryEvent> AppendAsync(MemoryEvent memoryEvent, CancellationToken cancellationToken = default); }
public interface IMemoryStore : IMemoryEventSink
{
    Task<IReadOnlyList<MemoryEvent>> ReadEventsAsync(string projectId, CancellationToken cancellationToken = default);
    Task SaveRecordAsync(MemoryRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryRecord>> ReadRecordsAsync(MemoryQuery query, CancellationToken cancellationToken = default);
    Task SaveCheckpointAsync(MemoryCheckpoint checkpoint, CancellationToken cancellationToken = default);
    Task<MemoryCheckpoint?> ReadCheckpointAsync(string projectId, string checkpointId, CancellationToken cancellationToken = default);
    Task RebuildProjectionsAsync(string projectId, CancellationToken cancellationToken = default);
}
public interface IMemoryRetriever
{
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(MemoryQuery query, CancellationToken cancellationToken = default);
    Task<MemoryContext> RewindAsync(string projectId, string checkpointId, MemoryQuery? query = null, CancellationToken cancellationToken = default);
}
public interface IMemoryContextBuilder
{
    Task<MemoryContext> BuildAsync(MemoryQuery query, int maxRecords = 20, int maxCharacters = 12000, int maxEvents = 50, int maxCitations = 40, CancellationToken cancellationToken = default);
}
public interface IMemoryProjector { Task<MemoryProjection> RebuildAsync(string projectId, CancellationToken cancellationToken = default); }
public interface IMemoryCheckpointStore { Task SaveAsync(MemoryCheckpoint checkpoint, CancellationToken cancellationToken = default); Task<MemoryCheckpoint?> GetAsync(string projectId, string checkpointId, CancellationToken cancellationToken = default); }
public interface IMemoryRedactor { RedactionResult Redact(string payload); }
