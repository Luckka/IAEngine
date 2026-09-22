using OnlineOs.AiOrchestrator.Roadmap;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class RoadmapTests
{
    [Fact]
    public void CheckedInM2RoadmapIsValidAndDependencyOrdered()
    {
        var catalog = RoadmapCatalog.Load(Path.Combine(FindRepository(), "ai", "roadmap", "MILESTONES.json"));
        var milestone = catalog.Get("M2");
        var state = new MilestoneRuntimeState { MilestoneId = "M2" };
        foreach (var task in milestone.Tasks) state.Tasks[task.Id] = new MilestoneTaskRuntime();

        Assert.Equal("M2-001", catalog.SelectNextTask(milestone, state)!.Id);
        state.Tasks["M2-001"].Status = MilestoneTaskStatus.Done;
        Assert.Equal("M2-002", catalog.SelectNextTask(milestone, state)!.Id);
    }

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void DuplicateTaskIdsAreRejected()
    {
        var document = new RoadmapDocument { Milestones = [new MilestoneDefinition { Id = "M", Title = "M", Tasks = [
            new RoadmapTaskDefinition { Id = "T", Title = "one" }, new RoadmapTaskDefinition { Id = "T", Title = "two" }] }] };
        Assert.Throws<InvalidOperationException>(() => RoadmapCatalog.Validate(document));
    }

    [Fact]
    public void MissingAndCyclicDependenciesAreRejected()
    {
        var missing = new RoadmapDocument { Milestones = [new MilestoneDefinition { Id = "M", Title = "M", Tasks = [
            new RoadmapTaskDefinition { Id = "T", Title = "task", DependsOn = ["NOPE"] }] }] };
        Assert.Throws<InvalidOperationException>(() => RoadmapCatalog.Validate(missing));
        var cyclic = new RoadmapDocument { Milestones = [new MilestoneDefinition { Id = "M", Title = "M", Tasks = [
            new RoadmapTaskDefinition { Id = "A", Title = "a", DependsOn = ["B"] }, new RoadmapTaskDefinition { Id = "B", Title = "b", DependsOn = ["A"] }] }] };
        Assert.Throws<InvalidOperationException>(() => RoadmapCatalog.Validate(cyclic));
    }

    [Fact]
    public async Task RoadmapStatePersistsAtomicallyAndSeparatelyFromDefinition()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"onlineos-roadmap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new RoadmapStateStore(directory);
            var state = new MilestoneRuntimeState { MilestoneId = "M2", RequiresHumanCheckpoint = true };
            await store.SaveAsync(state);
            var loaded = await store.LoadAsync();
            Assert.Equal("M2", loaded!.MilestoneId);
            Assert.True(loaded.RequiresHumanCheckpoint);
            Assert.False(File.Exists(Path.Combine(directory, ".ai-state", "roadmap-state.json.tmp")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
