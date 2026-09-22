using System.Net.Http.Json;
using System.Text.Json;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Agents;

public sealed class OllamaTaskRouter(HttpClient http, OllamaOptions options) : ITaskRouter, IFailureDiagnoser
{
    private static readonly string[] TaskTypes = ["feature", "bug", "documentation", "architecture", "refactor", "test", "security", "unknown"];
    private static readonly string[] Levels = ["low", "medium", "high"];
    private static readonly string[] Pipelines = ["local-only", "claude-only", "claude-codex", "human-required"];
    private static readonly string[] KnownDomains = ["service-order", "offline", "sync", "design", "ui", "security", "authentication", "architecture", "testing", "documentation", "general"];
    private static readonly string[] KnownSkills = ["ai/skills/flutter-feature/SKILL.md"];
    private static readonly string[] KnownContext = ["AGENTS.md", "CLAUDE.md", "docs/REQUIREMENTS.md", "docs/USER_FLOWS.md", "docs/DESIGN_SYSTEM.md", "docs/OFFLINE_SYNC.md", "docs/SECURITY.md", "docs/API_CONTRACTS.md", "docs/OPEN_QUESTIONS.md", "architecture/ARCHITECTURE.md", "architecture/adr/"];

    public async Task<RoutingResult> RouteAsync(DevelopmentTask task, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Model)) return Fallback(task, "No Ollama model is configured.");
        var prompt = $"""
            You are the local OnlineOS task router, not a coding agent. Classify and route the task below.
            Return ONLY one JSON object with exactly these fields and allowed values:
            type: feature | bug | documentation | architecture | refactor | test | security | unknown
            domains: string array
            complexity: low | medium | high
            risk: low | medium | high
            skills: string array
            relevantContext: string array
            recommendedPipeline: local-only | claude-only | claude-codex | human-required
            reasoningSummary: one short decision explanation
            Do not include Markdown, chain-of-thought, hidden reasoning, implementation, or extra fields.
            Use only applicable values from this lightweight project index:
            Domains: service-order, offline, sync, design, ui, security, authentication, architecture, testing, documentation, general.
            Skills: ai/skills/flutter-feature/SKILL.md.
            Context: AGENTS.md, CLAUDE.md, docs/REQUIREMENTS.md, docs/USER_FLOWS.md, docs/DESIGN_SYSTEM.md, docs/OFFLINE_SYNC.md, docs/SECURITY.md, docs/API_CONTRACTS.md, docs/OPEN_QUESTIONS.md, architecture/ARCHITECTURE.md, architecture/adr/.
            Prefer claude-codex for implementation or security work, local-only only for safe routing/document classification, and human-required for unresolved or dangerous tasks.
            Keep arrays short and do not invent application behavior.
            Task ID: {task.Id}
            Title: {task.Title}
            Description: {task.Description}
            Acceptance criteria: {string.Join("; ", task.AcceptanceCriteria ?? [])}
            """;

        string? lastError = null;
        for (var attempt = 0; attempt <= options.MaxResponseRetries; attempt++)
        {
            try
            {
                using var response = await http.PostAsJsonAsync("api/generate", new
                {
                    model = options.Model,
                    prompt,
                    stream = false,
                    format = "json",
                    think = options.Think,
                    options = new { temperature = options.Temperature, num_ctx = options.ContextSize, seed = options.Seed }
                }, ct);
                response.EnsureSuccessStatusCode();
                var envelope = await response.Content.ReadFromJsonAsync<OllamaEnvelope>(cancellationToken: ct);
                var route = Infrastructure.JsonSupport.DeserializeObject<RoutingResult>(envelope?.Response ?? "");
                if (IsValid(route)) return Normalize(route!, task);
                lastError = "Ollama returned malformed or incomplete routing JSON.";
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                lastError = exception.Message;
            }
        }
        return Fallback(task, lastError ?? "Ollama routing failed.");
    }

    public async Task<(bool Reachable, bool ModelAvailable, string Detail)> CheckHealthAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Model)) return (false, false, "ONLINEOS_OLLAMA_MODEL or Ollama.Model is required.");
        try
        {
            using var response = await http.GetAsync("api/tags", ct);
            if (!response.IsSuccessStatusCode) return (false, false, $"HTTP {(int)response.StatusCode}");
            var tags = await response.Content.ReadFromJsonAsync<OllamaTags>(cancellationToken: ct);
            var available = tags?.Models.Any(x => x.Name == options.Model || x.Name.StartsWith(options.Model + ":", StringComparison.Ordinal)) == true;
            return (true, available, available ? options.Model : $"Configured model '{options.Model}' is not installed.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            return (false, false, exception.Message);
        }
    }

    public async Task<DiagnosisSuggestion?> DiagnoseAsync(FailureContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Model)) return null;
        var payload = JsonSerializer.Serialize(new { stage = context.Stage, category = context.Category.ToString(), provider = context.Provider, exitCode = context.ExitCode, stderr = Tail(context.StandardError), stdout = Tail(context.StandardOutput), warnings = context.Warnings });
        try
        {
            using var response = await http.PostAsJsonAsync("api/generate", new { model = options.Model, prompt = $"Classify this sanitized failure. Return JSON only with suggestedCategory, likelyRootCause, suggestedRecovery, confidence. Allowed categories: {string.Join(",", Enum.GetNames<FailureCategory>())}. Evidence: {payload}", stream = false, format = "json", options = new { temperature = 0, seed = options.Seed } }, ct);
            if (!response.IsSuccessStatusCode) return null;
            var envelope = await response.Content.ReadFromJsonAsync<OllamaEnvelope>(cancellationToken: ct);
            var suggestion = Infrastructure.JsonSupport.DeserializeObject<DiagnosisSuggestion>(envelope?.Response ?? "");
            return suggestion is { Confidence: >= 0 and <= 1 } && Enum.TryParse<FailureCategory>(suggestion.SuggestedCategory, true, out _) ? suggestion : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException) { return null; }
    }

    private static string Tail(string value) => value.Length <= 2000 ? value : value[^2000..];

    private static bool IsValid(RoutingResult? route) => route is not null
        && TaskTypes.Contains(route.TaskType, StringComparer.OrdinalIgnoreCase)
        && Levels.Contains(route.Complexity, StringComparer.OrdinalIgnoreCase)
        && Levels.Contains(route.Risk, StringComparer.OrdinalIgnoreCase)
        && Pipelines.Contains(route.RecommendedPipeline, StringComparer.OrdinalIgnoreCase)
        && route.Domains is { Count: > 0 }
        && route.Domains.All(x => KnownDomains.Contains(x, StringComparer.OrdinalIgnoreCase))
        && route.Skills is not null
        && route.RelevantContext is not null
        && !string.IsNullOrWhiteSpace(route.ReasoningSummary);

    private static RoutingResult Normalize(RoutingResult route, DevelopmentTask task)
    {
        var domains = route.Domains
            .Where(x => KnownDomains.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Select(x => x.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var text = $"{task.Title} {task.Description}".ToLowerInvariant();
        if ((text.Contains("button") || text.Contains("radius") || text.Contains("design")) && !domains.Contains("ui")) domains.Add("ui");
        if ((text.Contains("button") || text.Contains("radius") || text.Contains("design")) && !domains.Contains("design")) domains.Add("design");
        if (text.Contains("offline") && !domains.Contains("offline")) domains.Add("offline");
        if ((text.Contains("synchron") || text.Contains("sync")) && !domains.Contains("sync")) domains.Add("sync");
        if ((text.Contains("token") || text.Contains("auth")) && !domains.Contains("security")) domains.Add("security");
        if ((text.Contains("token") || text.Contains("auth")) && !domains.Contains("authentication")) domains.Add("authentication");

        var elevatedRisk = domains.Contains("security") || domains.Contains("authentication") || (domains.Contains("offline") && domains.Contains("sync"));
        var implementationType = route.TaskType is "feature" or "bug" or "architecture" or "refactor" or "test" or "security";
        var policyChangedRisk = elevatedRisk && !route.Risk.Equals("high", StringComparison.OrdinalIgnoreCase);
        return route with
        {
            Domains = domains,
            Risk = elevatedRisk ? "high" : route.Risk.ToLowerInvariant(),
            Skills = route.Skills.Where(x => KnownSkills.Contains(x, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray(),
            RelevantContext = route.RelevantContext.Where(x => KnownContext.Contains(x, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray(),
            RecommendedPipeline = implementationType ? "claude-codex" : route.RecommendedPipeline.ToLowerInvariant(),
            ReasoningSummary = policyChangedRisk
                ? $"{route.ReasoningSummary.Trim()} Deterministic policy elevated risk to high."
                : route.ReasoningSummary.Trim()
        };
    }

    private static RoutingResult Fallback(DevelopmentTask task, string warning)
    {
        var text = $"{task.Title} {task.Description}".ToLowerInvariant();
        var domains = new List<string>();
        if (text.Contains("offline")) domains.Add("offline");
        if (text.Contains("sync")) domains.Add("sync");
        if (text.Contains("security") || text.Contains("auth")) domains.Add("security");
        if (domains.Count == 0) domains.Add("general");
        var type = TaskTypes.Contains(task.Type ?? "", StringComparer.OrdinalIgnoreCase) ? task.Type! : "unknown";
        var risk = Levels.Contains(task.Risk ?? "", StringComparer.OrdinalIgnoreCase) ? task.Risk! : "medium";
        return new RoutingResult(type, domains, "medium", risk,
            task.Skills ?? [], ["AGENTS.md", "CLAUDE.md", "architecture/ARCHITECTURE.md"],
            "claude-codex", "Conservative deterministic fallback routing.", true, warning);
    }

    private sealed record OllamaEnvelope(string Response);
    private sealed record OllamaTags(IReadOnlyList<OllamaModel> Models);
    private sealed record OllamaModel(string Name);
}
