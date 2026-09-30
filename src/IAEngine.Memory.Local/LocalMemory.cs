using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IAEngine.Core.Memory;
using Microsoft.Data.Sqlite;

namespace IAEngine.Memory.Local;

public sealed class LocalMemoryRedactor : IMemoryRedactor
{
    private static readonly Regex SensitiveKey = new("(access.?key|secret.?key|token|password|authorization|cookie|secret|connection.?string|financial|ssn|cpf)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SensitiveText = new("(Bearer\\s+[A-Za-z0-9._~+/=-]+|AKIA[0-9A-Z]{16}|(?:password|secret|token)\\s*[:=]\\s*[^\\s,;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public RedactionResult Redact(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return new(true, "{}", []);
        try
        {
            var node = JsonNode.Parse(payload);
            var limitations = new List<string>();
            RedactNode(node, limitations);
            return new(true, (node ?? new JsonObject()).ToJsonString(new JsonSerializerOptions { WriteIndented = false }), limitations);
        }
        catch (JsonException)
        {
            if (SensitiveText.IsMatch(payload)) return new(false, "{}", ["Payload was not persisted because it was not valid JSON and matched a sensitive pattern."]);
            return new(true, JsonSerializer.Serialize(new { value = payload }), ["Non-JSON payload was wrapped as a deterministic value."]);
        }
    }

    private static void RedactNode(JsonNode? node, List<string> limitations)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToList())
            {
                if (SensitiveKey.IsMatch(pair.Key)) { obj[pair.Key] = "[REDACTED]"; limitations.Add($"Redacted field '{pair.Key}'."); }
                else if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && SensitiveText.IsMatch(text)) { obj[pair.Key] = "[REDACTED]"; limitations.Add($"Redacted sensitive value in '{pair.Key}'."); }
                else RedactNode(pair.Value, limitations);
            }
        else if (node is JsonArray array)
            foreach (var child in array) RedactNode(child, limitations);
    }
}

public sealed class SqliteMemoryStore : IMemoryStore, IMemoryCheckpointStore, IMemoryProjector, IMemoryRetriever, IMemoryContextBuilder, IAsyncDisposable
{
    private readonly string connectionString;
    private readonly IMemoryRedactor redactor;
    private readonly SemaphoreSlim gate = new(1, 1);

    public SqliteMemoryStore(string databasePath, IMemoryRedactor? redactor = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
        this.redactor = redactor ?? new LocalMemoryRedactor();
        InitializeAsync().GetAwaiter().GetResult();
    }

    public async Task<MemoryEvent> AppendAsync(MemoryEvent memoryEvent, CancellationToken cancellationToken = default)
    {
        var redacted = redactor.Redact(memoryEvent.PayloadJson);
        var persisted = memoryEvent with { PayloadJson = redacted.SanitizedPayload };
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = Open();
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO memory_events (id,type,payload,project_id,run_id,milestone_id,task_id,created_at,correlation_id,causation_id,schema_version) VALUES ($id,$type,$payload,$project,$run,$milestone,$task,$created,$correlation,$causation,$version);";
            AddEventParameters(command, persisted);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await Project(persisted, cancellationToken);
            return persisted;
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<MemoryEvent>> ReadEventsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = Open(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,type,payload,project_id,run_id,milestone_id,task_id,created_at,correlation_id,causation_id,schema_version FROM memory_events WHERE project_id=$project ORDER BY created_at,id;";
        command.Parameters.AddWithValue("$project", projectId); return await ReadEvents(command, cancellationToken);
    }

    public async Task SaveRecordAsync(MemoryRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = Open(); await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO memory_records (id,project_id,run_id,milestone_id,task_id,source_event_id,memory_type,created_at,updated_at,status,confidence,summary,evidence,supersedes,superseded_by,tags) VALUES ($id,$project,$run,$milestone,$task,$source,$type,$created,$updated,$status,$confidence,$summary,$evidence,$supersedes,$superseded_by,$tags);";
        command.Parameters.AddWithValue("$id", record.MemoryId); command.Parameters.AddWithValue("$project", record.ProjectId); command.Parameters.AddWithValue("$run", (object?)record.RunId ?? DBNull.Value); command.Parameters.AddWithValue("$milestone", (object?)record.MilestoneId ?? DBNull.Value); command.Parameters.AddWithValue("$task", (object?)record.TaskId ?? DBNull.Value); command.Parameters.AddWithValue("$source", record.SourceEventId); command.Parameters.AddWithValue("$type", record.MemoryType.ToString()); command.Parameters.AddWithValue("$created", record.CreatedAt.ToString("O")); command.Parameters.AddWithValue("$updated", record.UpdatedAt.ToString("O")); command.Parameters.AddWithValue("$status", record.Status.ToString()); command.Parameters.AddWithValue("$confidence", record.Confidence); command.Parameters.AddWithValue("$summary", record.Summary); command.Parameters.AddWithValue("$evidence", record.Evidence); command.Parameters.AddWithValue("$supersedes", (object?)record.Supersedes ?? DBNull.Value); command.Parameters.AddWithValue("$superseded_by", (object?)record.SupersededBy ?? DBNull.Value); command.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(record.Tags ?? new Dictionary<string,string>()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryRecord>> ReadRecordsAsync(MemoryQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = Open(); await using var command = connection.CreateCommand(); var filters = new List<string>();
        void Add(string name, object? value, string clause) { if (value is not null) { filters.Add(clause); command.Parameters.AddWithValue(name, value); } }
        Add("$project", query.ProjectId, "project_id=$project"); Add("$milestone", query.MilestoneId, "milestone_id=$milestone"); Add("$task", query.TaskId, "task_id=$task"); Add("$type", query.MemoryType?.ToString(), "memory_type=$type"); Add("$status", query.Status?.ToString(), "status=$status"); Add("$source", query.SourceEventId, "source_event_id=$source"); Add("$from", query.From?.ToString("O"), "created_at >= $from"); Add("$to", query.To?.ToString("O"), "created_at <= $to");
        if (!string.IsNullOrWhiteSpace(query.Text)) { filters.Add("(summary LIKE $text OR evidence LIKE $text OR tags LIKE $text)"); command.Parameters.AddWithValue("$text", $"%{query.Text}%"); }
        command.CommandText = $"SELECT id,project_id,run_id,milestone_id,task_id,source_event_id,memory_type,created_at,updated_at,status,confidence,summary,evidence,supersedes,superseded_by,tags FROM memory_records {(filters.Count == 0 ? "" : "WHERE " + string.Join(" AND ", filters))} ORDER BY updated_at DESC,id LIMIT $limit;"; command.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 500));
        var records = new List<MemoryRecord>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) records.Add(ReadRecord(reader)); return records;
    }

    public async Task SaveCheckpointAsync(MemoryCheckpoint checkpoint, CancellationToken cancellationToken = default) { await using var c = Open(); await using var cmd = c.CreateCommand(); cmd.CommandText = "INSERT INTO memory_checkpoints (id,project_id,run_id,milestone_id,source_event_id,created_at,label,schema_version) VALUES ($id,$project,$run,$milestone,$source,$created,$label,$version);"; cmd.Parameters.AddWithValue("$id",checkpoint.CheckpointId);cmd.Parameters.AddWithValue("$project",checkpoint.ProjectId);cmd.Parameters.AddWithValue("$run",(object?)checkpoint.RunId??DBNull.Value);cmd.Parameters.AddWithValue("$milestone",(object?)checkpoint.MilestoneId??DBNull.Value);cmd.Parameters.AddWithValue("$source",checkpoint.SourceEventId);cmd.Parameters.AddWithValue("$created",checkpoint.CreatedAt.ToString("O"));cmd.Parameters.AddWithValue("$label",checkpoint.Label);cmd.Parameters.AddWithValue("$version",checkpoint.SchemaVersion); await cmd.ExecuteNonQueryAsync(cancellationToken); }
    public async Task<MemoryCheckpoint?> ReadCheckpointAsync(string projectId,string checkpointId,CancellationToken cancellationToken=default) { await using var c=Open();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT id,project_id,run_id,milestone_id,source_event_id,created_at,label,schema_version FROM memory_checkpoints WHERE project_id=$project AND id=$id";cmd.Parameters.AddWithValue("$project",projectId);cmd.Parameters.AddWithValue("$id",checkpointId);await using var r=await cmd.ExecuteReaderAsync(cancellationToken);if(!await r.ReadAsync(cancellationToken))return null;return new(r.GetString(0),r.GetString(1),NullableString(r,2),NullableString(r,3),r.GetString(4),DateTimeOffset.Parse(r.GetString(5)),r.GetString(6),r.GetInt32(7)); }
    public Task SaveAsync(MemoryCheckpoint checkpoint,CancellationToken cancellationToken=default)=>SaveCheckpointAsync(checkpoint,cancellationToken);
    public Task<MemoryCheckpoint?> GetAsync(string projectId,string checkpointId,CancellationToken cancellationToken=default)=>ReadCheckpointAsync(projectId,checkpointId,cancellationToken);
    public async Task RebuildProjectionsAsync(string projectId,CancellationToken cancellationToken=default) { await using var c=Open();await using var cmd=c.CreateCommand();cmd.CommandText="DELETE FROM memory_records WHERE project_id=$project";cmd.Parameters.AddWithValue("$project",projectId);await cmd.ExecuteNonQueryAsync(cancellationToken);foreach(var e in await ReadEventsAsync(projectId,cancellationToken)) await Project(e,cancellationToken); }
    public async Task<MemoryProjection> RebuildAsync(string projectId,CancellationToken cancellationToken=default) { await RebuildProjectionsAsync(projectId,cancellationToken);return new($"projection-{projectId}",projectId,"memory_records",DateTimeOffset.UtcNow,(await ReadEventsAsync(projectId,cancellationToken)).Count); }
    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(MemoryQuery query,CancellationToken cancellationToken=default) { var records=await ReadRecordsAsync(query,cancellationToken);var events=await ReadEventsAsync(query.ProjectId??records.FirstOrDefault()?.ProjectId??"",cancellationToken);return records.Select(r=>new MemorySearchResult(r,Score(r,query),[new($"citation-{r.MemoryId}",r.SourceEventId,null,"source event",r.Evidence)],events.FirstOrDefault(e=>e.EventId==r.SourceEventId),[],[])).OrderByDescending(x=>x.Score).ThenBy(x=>x.Memory.MemoryId,StringComparer.Ordinal).ToArray(); }
    public async Task<MemoryContext> RewindAsync(string projectId,string checkpointId,MemoryQuery? query=null,CancellationToken cancellationToken=default) { var cp=await ReadCheckpointAsync(projectId,checkpointId,cancellationToken)??throw new KeyNotFoundException($"Checkpoint '{checkpointId}' was not found.");var events=(await ReadEventsAsync(projectId,cancellationToken)).Where(e=>e.CreatedAt<=cp.CreatedAt).ToArray();var all=await ReadRecordsAsync((query??new MemoryQuery()) with { ProjectId=projectId });var allowed=all.Where(r=>events.Any(e=>e.EventId==r.SourceEventId)).ToArray();return BuildContext(allowed,events,20,12000,50,40); }
    public async Task<MemoryContext> BuildAsync(MemoryQuery query,int maxRecords=20,int maxCharacters=12000,int maxEvents=50,int maxCitations=40,CancellationToken cancellationToken=default) { var results=await SearchAsync(query,cancellationToken);var events=results.Select(x=>x.SourceEvent).Where(x=>x is not null).Cast<MemoryEvent>().Take(maxEvents).ToArray();return BuildContext(results.Select(x=>x.Memory).Take(maxRecords).ToArray(),events,maxRecords,maxCharacters,maxEvents,maxCitations); }
    public ValueTask DisposeAsync(){gate.Dispose();return ValueTask.CompletedTask;}

    private async Task InitializeAsync(){await using var c=Open();await using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS memory_events (id TEXT PRIMARY KEY,type TEXT NOT NULL,payload TEXT NOT NULL,project_id TEXT NOT NULL,run_id TEXT NOT NULL,milestone_id TEXT,task_id TEXT,created_at TEXT NOT NULL,correlation_id TEXT NOT NULL,causation_id TEXT,schema_version INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS memory_records (id TEXT PRIMARY KEY,project_id TEXT NOT NULL,run_id TEXT,milestone_id TEXT,task_id TEXT,source_event_id TEXT NOT NULL,memory_type TEXT NOT NULL,created_at TEXT NOT NULL,updated_at TEXT NOT NULL,status TEXT NOT NULL,confidence REAL NOT NULL,summary TEXT NOT NULL,evidence TEXT NOT NULL,supersedes TEXT,superseded_by TEXT,tags TEXT NOT NULL); CREATE TABLE IF NOT EXISTS memory_citations (id TEXT PRIMARY KEY,memory_id TEXT NOT NULL,source_event_id TEXT NOT NULL,artifact TEXT,label TEXT,excerpt TEXT); CREATE TABLE IF NOT EXISTS memory_checkpoints (id TEXT PRIMARY KEY,project_id TEXT NOT NULL,run_id TEXT,milestone_id TEXT,source_event_id TEXT NOT NULL,created_at TEXT NOT NULL,label TEXT NOT NULL,schema_version INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS memory_projections (id TEXT PRIMARY KEY,project_id TEXT NOT NULL,name TEXT NOT NULL,rebuilt_at TEXT NOT NULL,source_event_count INTEGER NOT NULL,schema_version INTEGER NOT NULL); CREATE INDEX IF NOT EXISTS ix_events_project_time ON memory_events(project_id,created_at); CREATE INDEX IF NOT EXISTS ix_records_project_type ON memory_records(project_id,memory_type); CREATE INDEX IF NOT EXISTS ix_records_milestone_task ON memory_records(milestone_id,task_id);";await cmd.ExecuteNonQueryAsync();}
    private SqliteConnection Open(){var c=new SqliteConnection(connectionString);c.Open();return c;}
    private static void AddEventParameters(SqliteCommand c,MemoryEvent e){c.Parameters.AddWithValue("$id",e.EventId);c.Parameters.AddWithValue("$type",e.Type.ToString());c.Parameters.AddWithValue("$payload",e.PayloadJson);c.Parameters.AddWithValue("$project",e.ProjectId);c.Parameters.AddWithValue("$run",e.RunId);c.Parameters.AddWithValue("$milestone",(object?)e.MilestoneId??DBNull.Value);c.Parameters.AddWithValue("$task",(object?)e.TaskId??DBNull.Value);c.Parameters.AddWithValue("$created",e.CreatedAt.ToString("O"));c.Parameters.AddWithValue("$correlation",e.CorrelationId);c.Parameters.AddWithValue("$causation",(object?)e.CausationId??DBNull.Value);c.Parameters.AddWithValue("$version",e.SchemaVersion);}
    private async Task<IReadOnlyList<MemoryEvent>> ReadEvents(SqliteCommand c,CancellationToken ct){var list=new List<MemoryEvent>();await using var r=await c.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))list.Add(new(r.GetString(0),Enum.Parse<MemoryEventType>(r.GetString(1)),r.GetString(2),r.GetString(3),r.GetString(4),NullableString(r,5),NullableString(r,6),DateTimeOffset.Parse(r.GetString(7)),r.GetString(8),NullableString(r,9),r.GetInt32(10)));return list;}
    private async Task Project(MemoryEvent e,CancellationToken ct){var payload=JsonNode.Parse(e.PayloadJson) as JsonObject;var summary=payload?["summary"]?.GetValue<string>()??$"{e.Type} recorded";var type=e.Type==MemoryEventType.ArchitectureDecision?MemoryType.Semantic:e.Type is MemoryEventType.TaskStarted or MemoryEventType.TaskCompleted or MemoryEventType.TaskFailed?MemoryType.Episodic:MemoryType.Working;var status=payload?["status"]?.GetValue<string>() is string s&&Enum.TryParse<MemoryStatus>(s,true,out var parsed)?parsed:MemoryStatus.Active;var supersedes=payload?["supersedes"]?.GetValue<string>();await SaveRecordAsync(new($"memory-{e.EventId}",e.ProjectId,e.RunId,e.MilestoneId,e.TaskId,e.EventId,type,e.CreatedAt,e.CreatedAt,status,1,summary,$"event:{e.EventId}",supersedes,null),ct);if(!string.IsNullOrWhiteSpace(supersedes)){await using var c=Open();await using var cmd=c.CreateCommand();cmd.CommandText="UPDATE memory_records SET status=$status,superseded_by=$new WHERE id=$old AND project_id=$project";cmd.Parameters.AddWithValue("$status",MemoryStatus.Superseded.ToString());cmd.Parameters.AddWithValue("$new",$"memory-{e.EventId}");cmd.Parameters.AddWithValue("$old",supersedes);cmd.Parameters.AddWithValue("$project",e.ProjectId);await cmd.ExecuteNonQueryAsync(ct);}}
    private static double Score(MemoryRecord r,MemoryQuery q)=>string.IsNullOrWhiteSpace(q.Text)?1:(r.Summary.Contains(q.Text,StringComparison.OrdinalIgnoreCase)?2:1);
    private static MemoryContext BuildContext(IReadOnlyList<MemoryRecord> records,IReadOnlyList<MemoryEvent> events,int maxRecords,int maxChars,int maxEvents,int maxCitations){var selected=new List<MemorySearchResult>();var chars=0;foreach(var r in records.OrderByDescending(r=>r.Status==MemoryStatus.Approved).ThenByDescending(r=>r.UpdatedAt).ThenBy(r=>r.MemoryId,StringComparer.Ordinal)){if(selected.Count>=maxRecords||selected.Sum(x=>x.Memory.Summary.Length)+r.Summary.Length>maxChars)continue;var citation=new MemoryCitation($"citation-{r.MemoryId}",r.SourceEventId,null,"source event",r.Evidence);selected.Add(new(r,1,[citation],events.FirstOrDefault(e=>e.EventId==r.SourceEventId),[],[]));chars+=r.Summary.Length;}var conflicts=selected.GroupBy(x=>x.Memory.Summary,StringComparer.OrdinalIgnoreCase).Where(g=>g.Select(x=>x.Memory.Status).Distinct().Count()>1).Select(g=>new MemoryConflict($"conflict-{Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(g.Key))}",g.Select(x=>x.Memory.MemoryId).ToArray(),$"Conflicting statuses for '{g.Key}'.")).ToArray();var citations=selected.SelectMany(x=>x.Citations).Take(maxCitations).ToArray();IReadOnlyList<string> limitations=selected.Count==0?new[]{"MEMORY_NOT_FOUND"}:conflicts.Length>0?new[]{"MEMORY_CONFLICT"}:Array.Empty<string>();return new(selected,citations,conflicts,limitations,"Project state reconstructed from persisted events.","Milestone state reconstructed from persisted events.");}
    private static MemoryRecord ReadRecord(SqliteDataReader r)=>new(r.GetString(0),r.GetString(1),NullableString(r,2),NullableString(r,3),NullableString(r,4),r.GetString(5),Enum.Parse<MemoryType>(r.GetString(6)),DateTimeOffset.Parse(r.GetString(7)),DateTimeOffset.Parse(r.GetString(8)),Enum.Parse<MemoryStatus>(r.GetString(9)),r.GetDouble(10),r.GetString(11),r.GetString(12),NullableString(r,13),NullableString(r,14));
    private static string? NullableString(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
}
