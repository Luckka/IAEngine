using System.Text.Json;

namespace OnlineOs.AiOrchestrator.Configuration;

public sealed class AppOptions
{
    public bool ProfileDeclared { get; internal set; }
    public ProjectProfileOptions Project { get; init; } = new();
    public OrchestratorOptions Orchestrator { get; init; } = new();
    public OllamaOptions Ollama { get; init; } = new();
    public ClaudeAgentOptions Claude { get; init; } = new();
    public CliAgentOptions Codex { get; init; } = new() { Command = "codex", Arguments = ["exec", "--sandbox", "read-only", "-"] };
    public ValidationOptions Validation { get; init; } = new();
    public GitOptions Git { get; init; } = new();
    public ReviewPolicyOptions ReviewPolicy { get; init; } = new();
    public ReferenceOptions Reference { get; init; } = new();
    public QaOptions Qa { get; init; } = new();
}

public sealed class ReferenceOptions
{
    public string? Root { get; init; }
    public string RequiredBranch { get; init; } = "";
    public string IndexPath { get; init; } = ".ai-state/reference-index.json";
    public string ArtifactName { get; init; } = "backend-reference.json";
    public bool Enabled { get; init; }
}

public sealed class OrchestratorOptions
{
    public int MaxRemediationCycles { get; init; } = 5;
    public int EngineeringRemediationCycles { get; init; } = 5;
    public int EngineeringProgressExtensions { get; init; } = 2;
    public int DeterministicValidationRemediationCycles { get; init; } = 5;
    public string RunsDirectory { get; init; } = ".ai-runs";
    public int ProcessTimeoutSeconds { get; init; } = 1800;
    public int ProviderTransientRetries { get; init; } = 3;
    public int MalformedOutputRetries { get; init; } = 2;
    public int UnknownDiagnosisRetries { get; init; } = 1;
    public int BackoffInitialSeconds { get; init; } = 15;
    public int BackoffMaxSeconds { get; init; } = 60;
    public int AutoWaitMaxMinutes { get; init; } = 15;
    public int PreflightDependencyRetries { get; init; } = 1;
    public RemediationContextOptions RemediationContext { get; init; } = new();
}

public sealed class RemediationContextOptions
{
    public int FocusedBudgetTokens { get; init; } = 120_000;
    public int MinimalBudgetTokens { get; init; } = 40_000;
    public int MaxReductionAttempts { get; init; } = 2;
    public double EstimatedTokensPerCharacter { get; init; } = 0.25;
    public double SafetyFactor { get; init; } = 1.25;
}

public sealed class OllamaOptions
{
    public string BaseUrl { get; init; } = "http://localhost:11434";
    public string Model { get; init; } = "";
    public int MaxResponseRetries { get; init; } = 1;
    public int TimeoutSeconds { get; init; } = 60;
    public double Temperature { get; init; } = 0;
    public int ContextSize { get; init; } = 4096;
    public int Seed { get; init; } = 42;
    public bool Think { get; init; }
    public bool AutoStart { get; init; } = true;
    public bool AutoPullConfiguredModel { get; init; }
}

public sealed class CliAgentOptions
{
    public string Command { get; init; } = "";
    public List<string> Arguments { get; init; } = [];
}

public sealed class ClaudeAgentOptions
{
    public string Command { get; init; } = "claude";
    public List<string> Arguments { get; init; } = ["-p", "--output-format", "json"];
    public List<string> AllowedTools { get; init; } = ["Read", "Edit", "Write", "Glob", "Grep", "Bash"];
}

public sealed class ValidationOptions
{
    public List<ValidationCommandOptions> Commands { get; init; } = [];
    public FlutterValidationOptions Flutter { get; init; } = new();
}

public sealed class ValidationCommandOptions
{
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public List<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public bool Required { get; init; } = true;
    public bool Enabled { get; init; } = true;
}

public sealed class FlutterValidationOptions
{
    public string ProjectPath { get; init; } = "";
    public string DeviceProbeCommand { get; init; } = "";
    public List<string> DeviceProbeArguments { get; init; } = [];
    public string? IntegrationDevice { get; init; }
}

public sealed class GitOptions
{
    public List<string> ProtectedBranches { get; init; } = ["main", "master"];
    public bool AllowProtectedBranch { get; init; }
}

public sealed class ReviewPolicyOptions
{
    public List<string> BlockingSeverities { get; init; } = ["CRITICAL"];
    public bool BlockingHighFails { get; init; } = true;
}

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static AppOptions Load(string baseDirectory)
    {
        return LoadFile(ResolveConfigPath(baseDirectory));
    }

    public static string ResolveConfigPath(string baseDirectory)
    {
        var configuredPath = Environment.GetEnvironmentVariable("ONLINEOS_ORCHESTRATOR_CONFIG");
        if (!string.IsNullOrWhiteSpace(configuredPath)) return Path.GetFullPath(configuredPath);

        var root = Path.GetFullPath(baseDirectory);
        var direct = Path.Combine(root, "appsettings.json");
        if (File.Exists(direct)) return direct;
        var legacy = Path.Combine(root, "tools", "ai-orchestrator", "appsettings.json");
        return File.Exists(legacy) ? legacy : direct;
    }

    public static AppOptions LoadFile(string path)
    {
        var hasProject = false;
        AppOptions options;
        if (File.Exists(path))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            hasProject = document.RootElement.TryGetProperty("Project", out var project)
                && project.ValueKind == JsonValueKind.Object;
            options = JsonSerializer.Deserialize<AppOptions>(document.RootElement.GetRawText(), JsonOptions) ?? new AppOptions();
        }
        else options = new AppOptions();

        options.ProfileDeclared = hasProject;

        return ApplyEnvironment(options);
    }

    private static AppOptions ApplyEnvironment(AppOptions options) => new()
    {
        ProfileDeclared = options.ProfileDeclared,
        Project = options.Project,
        Orchestrator = options.Orchestrator,
        Validation = options.Validation,
        Git = options.Git,
        ReviewPolicy = options.ReviewPolicy,
        Reference = new ReferenceOptions
        {
            Root = options.Project.IsOnlineOsCompatibilityProfile ? Environment.GetEnvironmentVariable("ONLINEOS_REFERENCE_ROOT") ?? options.Reference.Root : options.Reference.Root,
            RequiredBranch = options.Reference.RequiredBranch,
            IndexPath = options.Reference.IndexPath,
            ArtifactName = options.Reference.ArtifactName,
            Enabled = options.Reference.Enabled
        },
        Qa = options.Qa,
        Ollama = new OllamaOptions
        {
            BaseUrl = options.Project.IsOnlineOsCompatibilityProfile ? Environment.GetEnvironmentVariable("ONLINEOS_OLLAMA_BASE_URL") ?? options.Ollama.BaseUrl : options.Ollama.BaseUrl,
            Model = options.Project.IsOnlineOsCompatibilityProfile ? Environment.GetEnvironmentVariable("ONLINEOS_OLLAMA_MODEL") ?? options.Ollama.Model : options.Ollama.Model,
            MaxResponseRetries = options.Ollama.MaxResponseRetries,
            TimeoutSeconds = options.Ollama.TimeoutSeconds,
            Temperature = options.Ollama.Temperature,
            ContextSize = options.Ollama.ContextSize,
            Seed = options.Ollama.Seed,
            Think = options.Ollama.Think,
            AutoStart = options.Ollama.AutoStart,
            AutoPullConfiguredModel = options.Ollama.AutoPullConfiguredModel
        },
        Claude = options.Project.IsOnlineOsCompatibilityProfile ? OverrideClaudeCommand(options.Claude) : options.Claude,
        Codex = options.Project.IsOnlineOsCompatibilityProfile ? OverrideCommand("ONLINEOS_CODEX_COMMAND", options.Codex) : options.Codex
    };

    private static CliAgentOptions OverrideCommand(string variable, CliAgentOptions value) => new()
    {
        Command = Environment.GetEnvironmentVariable(variable) ?? value.Command,
        Arguments = value.Arguments
    };

    private static ClaudeAgentOptions OverrideClaudeCommand(ClaudeAgentOptions value) => new()
    {
        Command = Environment.GetEnvironmentVariable("ONLINEOS_CLAUDE_COMMAND") ?? value.Command,
        Arguments = value.Arguments,
        AllowedTools = value.AllowedTools
    };
}
