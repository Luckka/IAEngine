using System.Text.Json;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Agents;

public sealed class ClaudeAgent(IProcessRunner processes, ClaudeAgentOptions options, string repository, TimeSpan timeout, RemediationContextOptions? contextOptions = null) : IImplementationAgent
{
    public const string PermissionDeniedReason = "CLAUDE_PERMISSION_DENIED";
    public const string StructuredRefusalReason = "CLAUDE_IMPLEMENTATION_REFUSED";
    public const string PromptTooLongReason = "CLAUDE_PROMPT_TOO_LONG";
    private string Repository { get; } = Path.GetFullPath(repository);
    private RemediationContextBuilder ContextBuilder { get; } = new(contextOptions ?? new RemediationContextOptions(), repository);

    public Task<ImplementationResult> ImplementAsync(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering, CancellationToken ct = default)
        => ExecuteAsync(BuildImplementationPrompt(task, route, engineering), ct);

    public Task<ImplementationResult> RemediateAsync(DevelopmentTask task, EngineeringProfile engineering, IReadOnlyList<ValidationResult> validation, ReviewResult? review, CancellationToken ct = default, int contextReductionAttempt = 0)
    {
        var context = ContextBuilder.Build(task, engineering, validation, review, contextReductionAttempt);
        var prompt = $"""
            Remediate task {task.Id}: {task.Title}.
            Read and follow CLAUDE.md, AGENTS.md, relevant architecture and ADRs.
            Apply architecture/ENGINEERING_STANDARDS.md according to this deterministic engineering profile:
            {SerializeEngineeringProfile(engineering)}
            The bounded remediation context below is authoritative. Evaluate the current finding independently; fix it, preserve valid architecture, avoid unrelated refactors, and update tests where needed.
            {context.Prompt}
            You have broad terminal autonomy for normal development only inside the OnlineOS repository at {Repository}. Use Bash for repository-local development commands as needed, but do not access parent directories, sibling projects, production, secrets, or perform prohibited Git lifecycle/destructive operations. Read-only Git inspection is allowed; do not use git -C or paths targeting another repository, push, merge, rebase, checkout/switch to unrelated branches, reset --hard, destructive clean, branch deletion, deployment, or destructive database operations. The deterministic ValidationRunner remains authoritative.
            If requirements are contradictory, ambiguous, or unsafe to implement, do not return prose. Return the same JSON envelope with success=false, failureCode="CLAUDE_IMPLEMENTATION_REFUSED", structuredRefusal=true, summary="CLAUDE_IMPLEMENTATION_REFUSED", refusalReason containing only a concise safe explanation, filesChanged=[], testsChanged=[], and notApplicable as applicable. Never include chain-of-thought.
            Return JSON only with keys success (boolean), summary (string), failureCode (string or null), structuredRefusal (boolean), refusalReason (string or null), filesChanged (string array), testsChanged (string array), and notApplicable (object mapping category to concise reason).
            """;
        return ExecuteAsync(prompt, ct);
    }

    public static string BuildImplementationPrompt(DevelopmentTask task, RoutingResult route, EngineeringProfile engineering) => $"""
        Implement task {task.Id}: {task.Title}.
        {task.Description}
        Read and follow CLAUDE.md, AGENTS.md, and the canonical architecture/ENGINEERING_STANDARDS.md.
        Relevant domains: {string.Join(", ", route.Domains)}
        Relevant context: {string.Join(", ", route.RelevantContext)}
        Relevant skills: {string.Join(", ", route.Skills)}
        Acceptance criteria: {string.Join("; ", task.AcceptanceCriteria ?? [])}
        {task.ReferenceContext ?? "No monolith reference was required for this task."}
        Deterministic engineering applicability profile:
        {SerializeEngineeringProfile(engineering)}
        {BuildTaskSpecificInstructions(engineering)}
        Implement only the requested scope, inspect the diff, and report tests created or modified plus every NOT_APPLICABLE category and why.
        You have broad terminal autonomy for normal development only inside the configured OnlineOS repository working directory. Use Bash for Flutter/Dart/.NET/Node dependency, build, format, analyzer, test, code-generation, and read-only local Git commands as needed. Keep every path repository-relative or under this workspace. Do not access parent directories, sibling projects, another repository, production, or secrets. Do not push, merge, rebase, reset --hard, clean destructively, delete branches, deploy, or perform destructive database operations. The deterministic ValidationRunner remains authoritative after you finish.
        If requirements are contradictory, ambiguous, or unsafe to implement, return the same JSON envelope with success=false, failureCode="CLAUDE_IMPLEMENTATION_REFUSED", structuredRefusal=true, summary="CLAUDE_IMPLEMENTATION_REFUSED", refusalReason containing only a concise safe explanation, filesChanged=[], testsChanged=[], and notApplicable as applicable. Never return prose or chain-of-thought.
        Return JSON only with keys success (boolean), summary (string), failureCode (string or null), structuredRefusal (boolean), refusalReason (string or null), filesChanged (string array), testsChanged (string array), and notApplicable (object mapping category to concise reason).
        """;

    private static string BuildTaskSpecificInstructions(EngineeringProfile engineering)
    {
        if (!engineering.IsCodeTask)
            return "This is a non-code task. Do not add irrelevant implementation or test work; report coding and responsive categories as NOT_APPLICABLE with concise reasons.";

        var instructions = "Keep dependencies inward, apply SOLID pragmatically, write simple focused code, make behavior correct before refactoring, avoid overengineering, and preserve scope. Add behavior-focused unit tests, meaningful boundary integration tests, and regression-first coverage for bug fixes as required by the profile. Work RED-GREEN-REFACTOR where practical, but do not claim any test or analyzer passed; only the orchestrator's deterministic commands establish PASS.";
        if (engineering.IsFlutterUiTask)
            instructions += " For this Flutter UI task, design from available constraints; consider compact/medium/expanded equivalents, adaptive composition, Design System primitives, dynamic/localized content, text scaling/accessibility, keyboard-safe forms, and supported orientations. Add meaningful responsive widget/integration coverage across applicable small-phone, phone, large-phone, and tablet equivalents. MediaQuery, percentages, Flexible, or Expanded alone do not prove responsiveness.";
        return instructions;
    }

    private static string SerializeEngineeringProfile(EngineeringProfile engineering) =>
        JsonSerializer.Serialize(engineering, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });

    private async Task<ImplementationResult> ExecuteAsync(string prompt, CancellationToken ct)
    {
        var arguments = options.Arguments.Concat(["--allowedTools"]).Concat(options.AllowedTools).ToArray();
        var result = await processes.RunAsync(new ProcessSpec(options.Command, arguments, repository, prompt, timeout), ct);
        result = EnrichProviderMetadata(result, "anthropic");
        var terminalReason = ReadTerminalReason(result.StandardOutput);
        if (string.Equals(terminalReason, "prompt_too_long", StringComparison.OrdinalIgnoreCase)
            || string.Equals(terminalReason, "context_limit_exceeded", StringComparison.OrdinalIgnoreCase))
            return new ImplementationResult(false, "Claude request exceeded the provider context limit.", [], result, new UsageInfo("anthropic", null), FailureCode: PromptTooLongReason);
        if (!result.Succeeded) return new ImplementationResult(false, result.StandardError.Trim(), [], result, new UsageInfo("anthropic", null));
        var denials = ParsePermissionDenials(result.StandardOutput);
        var output = UnwrapClaudeOutput(result.StandardOutput);
        var parsed = JsonSupport.DeserializeObject<AgentReport>(output);
        if (parsed is null)
            return new ImplementationResult(false, "Claude returned malformed structured output.", [], result, new UsageInfo("anthropic", null), denials);
        if (denials.Any(x => x.Blocking) || (!parsed.Success && denials.Count > 0))
            return new ImplementationResult(false, PermissionDeniedReason, [], result, new UsageInfo("anthropic", null), denials);

        if (parsed.StructuredRefusal || string.Equals(parsed.FailureCode, StructuredRefusalReason, StringComparison.Ordinal))
            return new ImplementationResult(false, StructuredRefusalReason, [], result, new UsageInfo("anthropic", null), denials,
                [parsed.RefusalReason ?? "Claude reported that it could not safely proceed."], parsed.TestsChanged ?? [], parsed.NotApplicable,
                StructuredRefusalReason, true);

        var warnings = denials.Count == 0
            ? Array.Empty<string>()
            : ["Claude reported optional permission denials; implementation success requires observable repository changes."];
        return new ImplementationResult(parsed.Success, parsed.Summary ?? "", parsed.FilesChanged ?? [], result, new UsageInfo("anthropic", null), denials, warnings, parsed.TestsChanged ?? [], parsed.NotApplicable);
    }

    private static IReadOnlyList<PermissionDenial> ParsePermissionDenials(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("permission_denials", out var denials) || denials.ValueKind != JsonValueKind.Array)
                return [];
            return denials.EnumerateArray().Select(denial =>
            {
                var toolName = GetString(denial, "tool_name") ?? GetString(denial, "toolName");
                return new PermissionDenial(toolName, denial.GetRawText(), !IsOptionalGitInspection(toolName, denial));
            }).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsOptionalGitInspection(string? toolName, JsonElement denial)
    {
        if (!string.Equals(toolName, "Bash", StringComparison.OrdinalIgnoreCase)) return false;
        var input = denial.TryGetProperty("input", out var standardInput) ? standardInput
            : denial.TryGetProperty("tool_input", out var toolInput) ? toolInput
            : default;
        var command = input.ValueKind == JsonValueKind.Object ? GetString(input, "command") : GetString(denial, "command");
        if (string.IsNullOrWhiteSpace(command)) return false;
        var normalized = command.TrimStart();
        if (normalized.IndexOfAny([';', '&', '|', '>', '<', '\n', '\r']) >= 0) return false;
        return new[] { "git status", "git diff", "git show", "git log" }
            .Any(prefix => string.Equals(normalized, prefix, StringComparison.Ordinal)
                || normalized.StartsWith(prefix + " ", StringComparison.Ordinal));
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string UnwrapClaudeOutput(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
                return result.GetString() ?? output;
        }
        catch (JsonException) { }
        return output;
    }

    private static ProcessResult EnrichProviderMetadata(ProcessResult result, string provider)
    {
        var text = result.StandardOutput + " " + result.StandardError;
        var status = text.Contains("429", StringComparison.OrdinalIgnoreCase) || text.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ? 429
            : text.Contains("prompt_too_long", StringComparison.OrdinalIgnoreCase) || text.Contains("request size", StringComparison.OrdinalIgnoreCase) ? 400 : result.ProviderHttpStatus;
        return result with { Provider = result.Provider ?? provider, ProviderHttpStatus = status };
    }

    private static string? ReadTerminalReason(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            if (document.RootElement.TryGetProperty("terminal_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                return reason.GetString();
            if (document.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
            {
                using var nested = JsonDocument.Parse(result.GetString() ?? "");
                return nested.RootElement.TryGetProperty("terminal_reason", out var nestedReason) && nestedReason.ValueKind == JsonValueKind.String ? nestedReason.GetString() : null;
            }
        }
        catch (JsonException) { }
        return null;
    }

    private sealed record AgentReport(
        bool Success,
        string? Summary,
        string? FailureCode,
        bool StructuredRefusal,
        string? RefusalReason,
        IReadOnlyList<string>? FilesChanged,
        IReadOnlyList<string>? TestsChanged,
        IReadOnlyDictionary<string, string>? NotApplicable);
}
