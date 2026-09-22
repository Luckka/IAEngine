using System.Text.Json;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Infrastructure;

public sealed class GitLifecycleStore(string repositoryRoot, string directory = ".ai-state/git")
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private string PathFor(string id) => Path.Combine(repositoryRoot, directory, $"{id}.json");

    public async Task SaveAsync(string id, GitLifecycleMetadata metadata, CancellationToken ct = default)
    {
        var path = PathFor(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(metadata, Options), ct);
    }

    public async Task<GitLifecycleMetadata?> LoadAsync(string id, CancellationToken ct = default)
    {
        var path = PathFor(id);
        return !File.Exists(path) ? null : JsonSerializer.Deserialize<GitLifecycleMetadata>(await File.ReadAllTextAsync(path, ct), Options);
    }
}
