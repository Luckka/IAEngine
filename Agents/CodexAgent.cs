using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Agents;

public sealed class CodexAgent(IProcessRunner processes, CliAgentOptions options, string repository, TimeSpan timeout) : IReviewAgent
{
    public async Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, string gitDiff, CancellationToken ct = default)
        => await ReviewCoreAsync(task, engineering, [], gitDiff, ct);

    public async Task<ReviewResult> ReviewAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, string gitDiff, CancellationToken ct = default)
        => await ReviewCoreAsync(task, engineering, validation, gitDiff, ct);

    private async Task<ReviewResult> ReviewCoreAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, string gitDiff, CancellationToken ct)
    {
        var prompt = BuildReviewPrompt(task, engineering, validation, gitDiff);
        var result = await processes.RunAsync(new ProcessSpec(options.Command, options.Arguments, repository, prompt, timeout), ct);
        result = EnrichProviderMetadata(result);
        if (!result.Succeeded) return Failure("Codex process failed: " + result.StandardError.Trim(), result);
        var parsed = JsonSupport.DeserializeObject<ReviewPayload>(result.StandardOutput);
        if (parsed is null || parsed.Findings is null || parsed.Quality is null || string.IsNullOrWhiteSpace(parsed.Summary)) return Failure("Codex returned malformed structured output.", result);
        var normalizedQuality = NormalizeQuality(parsed.Quality, engineering);
        if (normalizedQuality is null) return Failure("Codex returned incomplete engineering quality categories.", result);
        foreach (var expectation in engineering.Standards)
        {
            var status = normalizedQuality.Single(x => x.Category.Equals(expectation.Category, StringComparison.OrdinalIgnoreCase)).Status;
            if ((expectation.Applicability == StandardApplicability.Required && status == QualityStatus.NotApplicable)
                || (expectation.Applicability == StandardApplicability.NotApplicable && status != QualityStatus.NotApplicable))
                return Failure("Codex returned quality applicability inconsistent with deterministic policy.", result);
        }
        return new ReviewResult(parsed.Decision, parsed.Findings, parsed.Summary, result, new UsageInfo("openai", null), normalizedQuality, PrototypeEvidence: parsed.PrototypeEvidence);
    }

    public static string BuildReviewPrompt(DevelopmentTask task, EngineeringProfile engineering, string gitDiff)
        => BuildReviewPrompt(task, engineering, [], gitDiff);

    public static string BuildReviewPrompt(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, string gitDiff) => $"""
            Act as the independent adversarial reviewer defined in ai/agents/reviewer.md.
            Review task {task.Id}: {task.Title}. Acceptance criteria: {string.Join("; ", task.AcceptanceCriteria ?? [])}
            Relevant context: {string.Join(", ", task.RelevantContext ?? [])}
            Backend compatibility context: {task.ReferenceContext ?? "No monolith reference was required for this task."}
            Read AGENTS.md, architecture/ENGINEERING_STANDARDS.md, relevant requirements, architecture, ADRs, tests, and the working tree diff. Do not modify any file.
            Independently evaluate this deterministic applicability profile; do not mark a REQUIRED category NOT_APPLICABLE without a concrete finding explaining why the classification is wrong:
            {SerializeEngineeringProfile(engineering)}
            Review every applicable general category: {string.Join(", ", EngineeringStandardsPolicy.GeneralReviewCategories)}.
            For Flutter UI tasks also review: {string.Join(", ", EngineeringStandardsPolicy.FlutterUiReviewCategories)}.
            Apply pragmatic severity: architecture boundary violations, missing acceptance criteria, missing required behavior/regression/integration/responsive coverage, security violations, scope violations, unusable supported viewports, overflow, clipped important localized content, inaccessible primary actions, and keyboard-blocked required forms can be HIGH/CRITICAL and blocking. Do not block on personal preferences or ceremonial abstractions.
            Deterministic validation results are authoritative. Never self-declare builds, analyzers, or tests passed based only on code inspection.
            Authoritative deterministic validation results:
            {System.Text.Json.JsonSerializer.Serialize(validation)}
            The current diff is included as evidence:
            {gitDiff}
            For required prototypeConformity, inspect the rendered prototype state and the equivalent Flutter emulator/Patrol state. Record both screenshot paths/state identifiers in prototypeEvidence with referenceState, referenceScreenshot, implementationState, and implementationScreenshot. Missing either side is a blocking conformity failure. Return JSON only with decision (PASS or FAIL), findings (array containing severity, file, location, problem, impact, recommendation, and blocking), quality (exactly one assessment for every category in engineering.Standards and no other categories; each assessment contains category, status PASS|FAIL|NOT_APPLICABLE, summary, and blocking), summary, and prototypeEvidence when the category is required. Do not put deterministic validation categories such as Format, StaticAnalysis, Build, UnitTests, IntegrationTests, or ResponsiveWidgetTests in quality; those are authoritative pipeline evidence and must not be self-reported.
            """;

    private static IReadOnlyList<QualityAssessment>? NormalizeQuality(
        IReadOnlyList<QualityAssessment> quality,
        EngineeringProfile engineering)
    {
        var standards = engineering.Standards.ToDictionary(x => x.Category, StringComparer.OrdinalIgnoreCase);
        var validationCategories = engineering.ExpectedValidation
            .Select(x => x.Category)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<QualityAssessment>(standards.Count);

        foreach (var assessment in quality)
        {
            if (standards.ContainsKey(assessment.Category))
            {
                if (normalized.Any(x => x.Category.Equals(assessment.Category, StringComparison.OrdinalIgnoreCase)))
                    return null;

                normalized.Add(assessment with { Category = standards[assessment.Category].Category });
                continue;
            }

            // Codex may redundantly echo deterministic validation results. They remain
            // provider audit evidence in the raw process output, but never participate
            // in the qualitative contract or override authoritative pipeline results.
            if (validationCategories.Contains(assessment.Category)) continue;
            return null;
        }

        return normalized.Count == standards.Count
            && standards.Keys.All(category => normalized.Any(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)))
            ? normalized
            : null;
    }

    private static string SerializeEngineeringProfile(EngineeringProfile engineering) =>
        System.Text.Json.JsonSerializer.Serialize(engineering, new System.Text.Json.JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });

    private static ReviewResult Failure(string message, ProcessResult process) => new(ReviewDecision.Fail,
        [new ReviewFinding(FindingSeverity.Critical, "", "", message, "Review could not be trusted.", "Inspect the CLI output and configuration.")], message, process, new UsageInfo("openai", null), IsProtocolFailure: true);

    private static ProcessResult EnrichProviderMetadata(ProcessResult result)
    {
        var text = result.StandardOutput + " " + result.StandardError;
        var status = text.Contains("429", StringComparison.OrdinalIgnoreCase) || text.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ? 429 : result.ProviderHttpStatus;
        return result with { Provider = result.Provider ?? "openai", ProviderHttpStatus = status };
    }

    private sealed record ReviewPayload(ReviewDecision Decision, IReadOnlyList<ReviewFinding>? Findings, IReadOnlyList<QualityAssessment>? Quality, string Summary, PrototypeReviewEvidence? PrototypeEvidence = null);
}
