namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed record PrototypeEvidenceRequirement(string StateId, string PrototypePath, string FlutterPath, string ManifestPath);
public sealed record PrototypeEvidenceCheck(bool Passed, string Code, IReadOnlyList<string> Missing);

/// Deterministic, reusable evidence gate. Review prose cannot override missing files.
public static class PrototypeEvidenceGate
{
    public static PrototypeEvidenceCheck Check(string repositoryRoot, string milestoneId, IEnumerable<string> states)
    {
        var missing = new List<string>();
        var evidenceRoot = Path.Combine(repositoryRoot, "docs", "qa", milestoneId.ToLowerInvariant(), "visual-evidence");
        var manifest = Path.Combine(evidenceRoot, "MANIFEST.md");
        foreach (var state in states.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var slug = state.ToLowerInvariant();
            var prototype = Path.Combine(evidenceRoot, "prototype", $"prototype-{slug}.png");
            var flutter = Path.Combine(evidenceRoot, "flutter", $"flutter-{slug}.png");
            if (!IsImage(prototype)) missing.Add($"PROTOTYPE_EVIDENCE_MISSING:{state}");
            if (!IsImage(flutter)) missing.Add($"FLUTTER_EVIDENCE_MISSING:{state}");
            if (!File.Exists(manifest) || !ManifestMaps(manifest, state, prototype, flutter))
                missing.Add($"PROTOTYPE_MANIFEST_MAPPING_MISSING:{state}");
        }
        return missing.Count == 0
            ? new PrototypeEvidenceCheck(true, "PROTOTYPE_EVIDENCE_PASS", missing)
            : new PrototypeEvidenceCheck(false, "PROTOTYPE_CONFORMITY_NOT_PROVEN", missing);
    }

    private static bool IsImage(string path) => File.Exists(path) && new FileInfo(path).Length > 0 &&
        string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);

    private static bool ManifestMaps(string manifest, string state, string prototype, string flutter)
    {
        var text = File.ReadAllText(manifest);
        return text.Contains(state, StringComparison.OrdinalIgnoreCase)
            && text.Contains(Path.GetFileName(prototype), StringComparison.OrdinalIgnoreCase)
            && text.Contains(Path.GetFileName(flutter), StringComparison.OrdinalIgnoreCase);
    }
}
