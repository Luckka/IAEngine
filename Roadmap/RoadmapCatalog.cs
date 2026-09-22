using System.Text.Json;
using OnlineOs.AiOrchestrator.Infrastructure;

namespace OnlineOs.AiOrchestrator.Roadmap;

public sealed class RoadmapCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public RoadmapDocument Document { get; }

    private RoadmapCatalog(RoadmapDocument document) => Document = document;

    public static RoadmapCatalog Load(string path)
    {
        var document = JsonSerializer.Deserialize<RoadmapDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("Roadmap is empty or malformed.");
        Validate(document);
        return new RoadmapCatalog(document);
    }

    public MilestoneDefinition Get(string id) => Document.Milestones.FirstOrDefault(x => x.Id == id)
        ?? throw new InvalidOperationException($"Milestone '{id}' was not found in the roadmap.");

    public IReadOnlyList<RoadmapTaskDefinition> GetReadyTasks(MilestoneDefinition milestone, MilestoneRuntimeState state)
        => milestone.Tasks.Where(task => state.Tasks.TryGetValue(task.Id, out var current)
            && current.Status is MilestoneTaskStatus.Pending or MilestoneTaskStatus.Ready
            && task.DependsOn.All(dependency => state.Tasks.TryGetValue(dependency, out var dependencyState) && dependencyState.Status == MilestoneTaskStatus.Done)).ToArray();

    public RoadmapTaskDefinition? SelectNextTask(MilestoneDefinition milestone, MilestoneRuntimeState state)
        => GetReadyTasks(milestone, state).FirstOrDefault();

    public static void Validate(RoadmapDocument document)
    {
        if (document.Milestones.Count == 0) throw new InvalidOperationException("Roadmap must contain at least one milestone.");
        var milestones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var milestone in document.Milestones)
        {
            if (!milestones.Add(milestone.Id)) throw new InvalidOperationException($"Duplicate milestone ID: {milestone.Id}.");
            if (string.IsNullOrWhiteSpace(milestone.Title) || (milestone.Tasks.Count == 0 && !string.Equals(milestone.Status, "placeholder", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Milestone '{milestone.Id}' must have a title and at least one task.");
            var tasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var task in milestone.Tasks)
                if (!tasks.Add(task.Id)) throw new InvalidOperationException($"Duplicate task ID: {task.Id}.");
            foreach (var task in milestone.Tasks)
                foreach (var dependency in task.DependsOn)
                    if (!tasks.Contains(dependency)) throw new InvalidOperationException($"Task '{task.Id}' depends on missing task '{dependency}'.");

            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var task in milestone.Tasks) Visit(task.Id, milestone, visiting, visited);
        }
    }

    private static void Visit(string id, MilestoneDefinition milestone, HashSet<string> visiting, HashSet<string> visited)
    {
        if (visited.Contains(id)) return;
        if (!visiting.Add(id)) throw new InvalidOperationException($"Circular dependency detected at task '{id}'.");
        var task = milestone.Tasks.Single(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        foreach (var dependency in task.DependsOn) Visit(dependency, milestone, visiting, visited);
        visiting.Remove(id);
        visited.Add(id);
    }
}

public sealed class RoadmapStateStore(string repositoryRoot)
{
    public string RepositoryRoot { get; } = Path.GetFullPath(repositoryRoot);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private readonly string path = new WorkspaceBoundary(repositoryRoot).Resolve(".ai-state/roadmap-state.json");

    public async Task<MilestoneRuntimeState?> LoadAsync(CancellationToken ct = default)
        => File.Exists(path) ? JsonSerializer.Deserialize<MilestoneRuntimeState>(await File.ReadAllTextAsync(path, ct), JsonOptions) : null;

    public async Task SaveAsync(MilestoneRuntimeState state, CancellationToken ct = default)
    {
        state.UpdatedAt = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, JsonOptions), ct);
        File.Move(temporary, path, true);
    }
}
