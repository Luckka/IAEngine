using IAEngine.Core.Memory;
using IAEngine.Memory.Local;

namespace IAEngine.Memory.Local.Tests;

public sealed class MemoryTests
{
    [Fact]
    public async Task EventsAreAppendOnlyAndSurviveReopen()
    {
        var dir = Directory.CreateTempSubdirectory("m21-"); var path = Path.Combine(dir.FullName, "memory.sqlite");
        try
        {
            var id = Guid.NewGuid().ToString("N");
            await using (var store = new SqliteMemoryStore(path)) await store.AppendAsync(Event(id, MemoryEventType.ArchitectureDecision, "Use ProjectReference"));
            await using (var reopened = new SqliteMemoryStore(path)) { var events = await reopened.ReadEventsAsync("p"); Assert.Single(events); Assert.Equal(id, events[0].EventId); }
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task RedactionNeverPersistsSecrets()
    {
        var dir = Directory.CreateTempSubdirectory("m21-");
        try
        {
            await using var store = new SqliteMemoryStore(Path.Combine(dir.FullName, "memory.sqlite"));
            var e = await store.AppendAsync(Event("secret-event", MemoryEventType.AgentResponse, "{\"access_key\":\"AKIA1234567890123456\",\"summary\":\"safe\"}"));
            Assert.DoesNotContain("AKIA", e.PayloadJson, StringComparison.Ordinal); Assert.Contains("REDACTED", e.PayloadJson, StringComparison.Ordinal);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task RetrievalIsDeterministicAndCitesSource()
    {
        var dir = Directory.CreateTempSubdirectory("m21-");
        try
        {
            await using var store = new SqliteMemoryStore(Path.Combine(dir.FullName, "memory.sqlite"));
            var e = await store.AppendAsync(Event("decision", MemoryEventType.ArchitectureDecision, "{\"summary\":\"AWS provider remains behind InfraSentinel\",\"status\":\"Approved\"}")); await store.RebuildProjectionsAsync("p");
            var results = await store.SearchAsync(new MemoryQuery("AWS", "p")); Assert.Single(results); Assert.Equal(e.EventId, results[0].Memory.SourceEventId); Assert.Single(results[0].Citations);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task RewindExcludesLaterEventsWithoutChangingLog()
    {
        var dir = Directory.CreateTempSubdirectory("m21-");
        try
        {
            await using var store = new SqliteMemoryStore(Path.Combine(dir.FullName, "memory.sqlite"));
            var first = await store.AppendAsync(Event("first", MemoryEventType.ArchitectureDecision, "{\"summary\":\"Decision A\"}", DateTimeOffset.UtcNow.AddMinutes(-2))); var checkpoint = new MemoryCheckpoint("cp", "p", "r", "m", first.EventId, first.CreatedAt, "before B"); await store.SaveCheckpointAsync(checkpoint); await store.AppendAsync(Event("later", MemoryEventType.ArchitectureDecision, "{\"summary\":\"Decision B\"}", DateTimeOffset.UtcNow)); await store.RebuildProjectionsAsync("p");
            var context = await store.RewindAsync("p", "cp"); Assert.Contains(context.RelevantMemories, x => x.Memory.Summary.Contains("Decision A")); Assert.DoesNotContain(context.RelevantMemories, x => x.Memory.Summary.Contains("Decision B")); Assert.Equal(2, (await store.ReadEventsAsync("p")).Count);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task SupersededDecisionAndNotFoundAreExplicit()
    {
        var dir = Directory.CreateTempSubdirectory("m21-");
        try
        {
            await using var store = new SqliteMemoryStore(Path.Combine(dir.FullName, "memory.sqlite"));
            await store.AppendAsync(Event("decision-a", MemoryEventType.ArchitectureDecision, "{\"summary\":\"Use ProjectReference\"}"));
            await store.AppendAsync(Event("decision-b", MemoryEventType.ArchitectureDecision, "{\"summary\":\"Use local package\",\"supersedes\":\"memory-decision-a\",\"status\":\"Approved\"}"));
            var records = await store.ReadRecordsAsync(new MemoryQuery(ProjectId: "p"));
            Assert.Contains(records, x => x.Status == MemoryStatus.Superseded && x.SupersededBy == "memory-decision-b");
            var context = await store.BuildAsync(new MemoryQuery(Text: "does-not-exist", ProjectId: "p")); Assert.Contains("MEMORY_NOT_FOUND", context.Limitations);
        }
        finally { dir.Delete(true); }
    }

    private static MemoryEvent Event(string id, MemoryEventType type, string payload, DateTimeOffset? at = null) => new(id, type, payload, "p", "r", "m", "t", at ?? DateTimeOffset.UtcNow, "c", null);
}
