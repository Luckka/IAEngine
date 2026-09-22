using System.Text.Json;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed class MilestoneCatalog
{
    private readonly Dictionary<string, MilestoneDefinition> milestones;
    private MilestoneCatalog(IEnumerable<MilestoneDefinition> definitions) => milestones = definitions.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
    public bool TryGet(string id, out MilestoneDefinition milestone) => milestones.TryGetValue(id, out milestone!);

    public static MilestoneCatalog Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Milestone roadmap was not found.", path);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var definitions = document.RootElement.GetProperty("milestones").EnumerateArray().Select(item =>
            new MilestoneDefinition(item.GetProperty("id").GetString()!, item.GetProperty("title").GetString()!, item.GetProperty("branch").GetString()!));
        return new MilestoneCatalog(definitions);
    }
}
