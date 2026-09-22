namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed record VisualConformityReviewCheck(
    bool Passed,
    string Code,
    IReadOnlyList<string> Missing);

/// Independent review gate. It is intentionally separate from
/// PrototypeEvidenceGate: valid PNGs prove that evidence exists, never that
/// the two screens conform.
public static class VisualConformityReviewGate
{
    private static readonly string[] RequiredFields =
    [
        "Prototype evidence inspected:",
        "Flutter evidence inspected:",
        "Same product state confirmed:",
        "Missing structural elements:",
        "Invented structural elements:",
        "Hierarchy differences:",
        "Navigation differences:",
        "CTA differences:",
        "State representation differences:",
        "Final classification:"
    ];

    public static VisualConformityReviewCheck Check(
        string repositoryRoot,
        string milestoneId,
        IEnumerable<string> states)
    {
        var path = Path.Combine(repositoryRoot, "docs", "qa",
            milestoneId.ToLowerInvariant(), "visual-evidence", "CONFORMITY_REVIEW.md");
        if (!File.Exists(path))
            return new(false, "VISUAL_CONFORMITY_REVIEW_MISSING", [path]);

        var text = File.ReadAllText(path);
        var missing = new List<string>();
        foreach (var state in states.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var marker = $"State: {state}";
            var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                missing.Add($"VISUAL_REVIEW_MISSING:{state}");
                continue;
            }
            var end = text.IndexOf("\nState:", start + marker.Length,
                StringComparison.OrdinalIgnoreCase);
            var section = text[start..(end < 0 ? text.Length : end)];
            foreach (var field in RequiredFields)
                if (!section.Contains(field, StringComparison.OrdinalIgnoreCase))
                    missing.Add($"VISUAL_REVIEW_FIELD_MISSING:{state}:{field}");

            if (!HasYes(section, "Prototype evidence inspected:") ||
                !HasYes(section, "Flutter evidence inspected:") ||
                !HasYes(section, "Same product state confirmed:"))
                missing.Add($"VISUAL_REVIEW_EVIDENCE_NOT_CONFIRMED:{state}");

            var classification = ReadValue(section, "Final classification:");
            if (classification is "MAJOR" or "BLOCKER")
                missing.Add($"VISUAL_CONFORMITY_{classification}:{state}");
            else if (classification is not ("PASS" or "MINOR"))
                missing.Add($"VISUAL_REVIEW_CLASSIFICATION_INVALID:{state}");
        }

        return missing.Count == 0
            ? new(true, "VISUAL_CONFORMITY_REVIEW_PASS", missing)
            : new(false, "VISUAL_CONFORMITY_NOT_PROVEN", missing);
    }

    private static bool HasYes(string section, string field) =>
        ReadValue(section, field).Equals("YES", StringComparison.OrdinalIgnoreCase);

    private static string ReadValue(string section, string field)
    {
        var start = section.IndexOf(field, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;
        var value = section[(start + field.Length)..];
        var newline = value.IndexOf('\n');
        return value[..(newline < 0 ? value.Length : newline)].Trim();
    }
}
