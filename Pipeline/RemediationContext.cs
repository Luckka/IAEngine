using System.Text;
using System.Text.Json;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed record RemediationContextPackage(string Prompt, string Strategy, long EstimatedTokens, IReadOnlyList<string> IncludedFiles);

/// Selects authoritative, finding-focused context before an agent is invoked. The
/// selector is intentionally provider-neutral so retrieval can replace file scans later.
public sealed class RemediationContextBuilder(RemediationContextOptions options, string repository)
{
    public RemediationContextPackage Build(DevelopmentTask task, EngineeringProfile engineering,
        IReadOnlyList<ValidationResult> validation, ReviewResult? review, int reductionAttempt)
    {
        var strategy = reductionAttempt <= 0 ? "focused" : "minimal";
        var budget = reductionAttempt <= 0 ? options.FocusedBudgetTokens : options.MinimalBudgetTokens;
        var sections = new List<(string Name, int Priority, string Text, string? File)>
        {
            ("blocking finding", 0, FormatFindings(review), null),
            ("task and requirements", 1, $"Task: {task.Id} — {task.Title}\nObjective: {task.Description}\nRequirements/acceptance: {string.Join("; ", task.AcceptanceCriteria ?? [])}", null),
            ("engineering constraints", 4, JsonSerializer.Serialize(engineering), null)
        };

        var files = (review?.Findings ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.File))
            .Select(x => x.File.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var file in files)
        {
            var fullPath = Path.GetFullPath(Path.Combine(repository, file));
            if (!fullPath.StartsWith(Path.GetFullPath(repository) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (!File.Exists(fullPath)) continue;
            sections.Add(("target source: " + file, 2, ReadBounded(fullPath, 80_000), file));
        }

        var relatedTests = Directory.Exists(Path.Combine(repository, "app"))
            ? Directory.EnumerateFiles(Path.Combine(repository, "app"), "*_test.dart", SearchOption.AllDirectories)
                .Where(x => files.Any(f => Path.GetFileNameWithoutExtension(x).Contains(Path.GetFileNameWithoutExtension(f), StringComparison.OrdinalIgnoreCase)))
                .Select(x => Path.GetRelativePath(repository, x).Replace('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(4)
            : [];
        foreach (var file in relatedTests)
            sections.Add(("relevant test: " + file, 3, ReadBounded(Path.Combine(repository, file), 50_000), file));

        sections.Add(("applicable reference", 5, "Use backend/reference context only when directly required by the finding. Do not invent endpoints or modify the reference repository.", null));
        var selected = new List<(string Name, string Text, string? File)>();
        var charsBudget = (int)(budget / (options.EstimatedTokensPerCharacter * options.SafetyFactor));
        var used = 0;
        foreach (var section in sections.OrderBy(x => x.Priority))
        {
            var text = section.Text;
            if (section.Priority == 1) text = text[..Math.Min(text.Length, reductionAttempt == 0 ? 4_000 : 800)];
            if (used + text.Length > charsBudget)
            {
                if (section.Priority <= 3 && used < charsBudget)
                    text = text[..Math.Min(text.Length, charsBudget - used)];
                else continue;
            }
            selected.Add((section.Name, text, section.File));
            used += text.Length;
        }

        var prompt = $"""
            Focused remediation ({strategy}) for task {task.Id}.
            Fix only the current unresolved Codex finding. Preserve architecture and existing contracts.
            Context budget: {budget} estimated tokens; estimated selected context: {EstimateTokens(string.Join("\n", selected.Select(x => x.Text)))}.
            {string.Join("\n\n", selected.Select(x => $"[{x.Name}]\n{x.Text}"))}
            Run deterministic validation after editing and report JSON only in the required envelope.
            """;
        return new RemediationContextPackage(prompt, strategy, EstimateTokens(prompt), selected.Where(x => x.File is not null).Select(x => x.File!).ToArray());
    }

    private long EstimateTokens(string text) => (long)Math.Ceiling(text.Length * options.EstimatedTokensPerCharacter * options.SafetyFactor);
    private static string ReadBounded(string path, int maxChars)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[maxChars];
        var count = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, count);
    }
    private static string FormatFindings(ReviewResult? review) => review is null
        ? "No Codex review finding; address the deterministic validation failures below."
        : string.Join("\n", review.Findings.Where(x => x.Blocking).Select(x => $"{x.Severity} {x.File}:{x.Location} — {x.Problem} Impact: {x.Impact} Recommendation: {x.Recommendation}"));
}
