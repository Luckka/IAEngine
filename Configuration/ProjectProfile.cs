namespace OnlineOs.AiOrchestrator.Configuration;

/// <summary>
/// Declarative, data-only identity and policy inputs for a project consuming the engine.
/// Technology-specific options remain in their existing compatibility sections for now.
/// </summary>
public sealed class ProjectProfileOptions
{
    public string Id { get; init; } = "engine-project";
    public string WorkspaceRoot { get; init; } = ".";
    public string Stack { get; init; } = "unspecified";
    public List<string> Commands { get; init; } = [];
    public List<string> Validators { get; init; } = [];
    public List<string> Policies { get; init; } = [];
    public BranchPolicyOptions BranchPolicy { get; init; } = new();
    public RetryLimitOptions RetryLimits { get; init; } = new();
    public RemediationLimitOptions RemediationLimits { get; init; } = new();
    public int TimeoutSeconds { get; init; } = 1800;
    public List<string> ContextPaths { get; init; } = [];
    public ProjectCompositionOptions Composition { get; init; } = new();

    public bool IsOnlineOsCompatibilityProfile => string.Equals(Id, "onlineos-mobile", StringComparison.OrdinalIgnoreCase);
}

public sealed class ProjectCompositionOptions
{
    public List<string> Providers { get; init; } = [];
    public List<string> Capabilities { get; init; } = [];
}

public sealed class BranchPolicyOptions
{
    public List<string> ProtectedBranches { get; init; } = ["main", "master"];
    public bool AllowProtectedBranch { get; init; }
}

public sealed class RetryLimitOptions
{
    public int ProviderTransient { get; init; } = 3;
    public int MalformedOutput { get; init; } = 2;
    public int UnknownDiagnosis { get; init; } = 1;
}

public sealed class RemediationLimitOptions
{
    public int ValidationCycles { get; init; } = 5;
    public int EngineeringCycles { get; init; } = 5;
    public int ProgressExtensions { get; init; } = 2;
}

public static class ProjectProfileValidator
{
    public static IReadOnlyList<string> Validate(ProjectProfileOptions profile)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.Id)) errors.Add("Project profile id is required.");
        if (string.IsNullOrWhiteSpace(profile.WorkspaceRoot)) errors.Add("Project profile workspace root is required.");
        if (profile.TimeoutSeconds <= 0) errors.Add("Project profile timeout must be positive.");
        if (profile.RetryLimits.ProviderTransient < 0 || profile.RetryLimits.MalformedOutput < 0 || profile.RetryLimits.UnknownDiagnosis < 0)
            errors.Add("Project profile retry limits cannot be negative.");
        if (profile.RemediationLimits.ValidationCycles < 0 || profile.RemediationLimits.EngineeringCycles < 0 || profile.RemediationLimits.ProgressExtensions < 0)
            errors.Add("Project profile remediation limits cannot be negative.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateAppOptions(AppOptions options)
    {
        var errors = new List<string>();
        if (!options.ProfileDeclared)
        {
            errors.Add("Project profile is required; define the Project section before starting the Engine.");
            return errors;
        }

        errors.AddRange(Validate(options.Project));
        if (!options.Project.IsOnlineOsCompatibilityProfile)
        {
            errors.AddRange(OnlineOsTokenInspection.Find(options));
            return errors;
        }

        if (!string.Equals(options.Project.Stack, "flutter", StringComparison.OrdinalIgnoreCase))
            errors.Add("OnlineOS compatibility profile requires Project.Stack 'flutter'.");
        if (options.Project.Commands.Count == 0) errors.Add("OnlineOS compatibility profile requires Project.Commands.");
        if (options.Project.Validators.Count == 0) errors.Add("OnlineOS compatibility profile requires Project.Validators.");
        if (string.IsNullOrWhiteSpace(options.Qa.ProjectPath)) errors.Add("OnlineOS compatibility profile requires Qa.ProjectPath.");
        if (string.IsNullOrWhiteSpace(options.Qa.Device)) errors.Add("OnlineOS compatibility profile requires Qa.Device.");
        if (string.IsNullOrWhiteSpace(options.Qa.PatrolCommand) || string.IsNullOrWhiteSpace(options.Qa.AdbCommand))
            errors.Add("OnlineOS compatibility profile requires Qa.PatrolCommand and Qa.AdbCommand.");
        if (options.Validation.Commands.Count == 0) errors.Add("OnlineOS compatibility profile requires Validation.Commands.");
        if (string.IsNullOrWhiteSpace(options.Validation.Flutter.ProjectPath)) errors.Add("OnlineOS compatibility profile requires Validation.Flutter.ProjectPath.");
        if (string.IsNullOrWhiteSpace(options.Reference.RequiredBranch)) errors.Add("OnlineOS compatibility profile requires Reference.RequiredBranch.");
        if (options.Git.ProtectedBranches.Count == 0) errors.Add("OnlineOS compatibility profile requires Git.ProtectedBranches.");
        foreach (var component in new[] { "ollama", "claude", "codex" })
            if (!options.Project.Composition.Providers.Contains(component, StringComparer.OrdinalIgnoreCase))
                errors.Add($"OnlineOS compatibility profile requires provider '{component}' in Project.Composition.Providers.");
        foreach (var capability in new[] { "qa", "reference-inspection", "git-workflow" })
            if (!options.Project.Composition.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase))
                errors.Add($"OnlineOS compatibility profile requires capability '{capability}' in Project.Composition.Capabilities.");
        if (!options.Project.Composition.Capabilities.Contains("validation", StringComparer.OrdinalIgnoreCase))
            errors.Add("OnlineOS compatibility profile requires capability 'validation' in Project.Composition.Capabilities.");
        return errors;
    }
}

public interface IProjectComposition
{
    string ProjectId { get; }
    bool IsOnlineOsCompatibility { get; }
    IReadOnlyList<string> Providers { get; }
    IReadOnlyList<string> Validators { get; }
    IReadOnlyList<string> Policies { get; }
    IReadOnlyList<string> Capabilities { get; }
}

public sealed record EngineCompositionPlan(
    string ProjectId,
    bool IsOnlineOsCompatibility,
    bool RegistersValidation,
    bool RegistersQa,
    bool RegistersReferenceInspection,
    bool RegistersOnlineOsPolicies,
    IReadOnlyList<string> ContextPaths,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> Validators,
    IReadOnlyList<string> Policies,
    IReadOnlyList<string> Capabilities) : IProjectComposition;

public static class ProjectCompositionExtensions
{
    public static EngineCompositionRuntimeBuilder CreateRuntimeBuilder(this IProjectComposition composition)
        => new(composition);
}

public static class EngineComposition
{
    public static EngineCompositionPlan Create(AppOptions options)
    {
        var onlineOs = options.Project.IsOnlineOsCompatibilityProfile;
        return new EngineCompositionPlan(
            options.Project.Id,
            onlineOs,
            options.Project.Validators.Count > 0 || options.Project.Composition.Capabilities.Contains("validation", StringComparer.OrdinalIgnoreCase),
            options.Project.Composition.Capabilities.Contains("qa", StringComparer.OrdinalIgnoreCase),
            options.Project.Composition.Capabilities.Contains("reference-inspection", StringComparer.OrdinalIgnoreCase),
            onlineOs,
            onlineOs ? options.Project.ContextPaths : [],
            options.Project.Composition.Providers,
            options.Project.Validators,
            options.Project.Policies,
            options.Project.Composition.Capabilities);
    }
}

internal static class OnlineOsTokenInspection
{
    private static readonly string[] ForbiddenTokens = [
        "onlineos", "online os", "b1208", "producao", "developer", "flutter", "patrol", "dart", "prototype", "ollama"];

    public static IReadOnlyList<string> Find(AppOptions options)
    {
        var values = new List<string>();
        values.AddRange(options.Project.Commands);
        values.AddRange(options.Project.Validators);
        values.AddRange(options.Project.Policies);
        values.AddRange(options.Project.ContextPaths);
        values.AddRange(options.Project.Composition.Providers);
        values.AddRange(options.Project.Composition.Capabilities);
        values.AddRange(options.Project.BranchPolicy.ProtectedBranches);
        values.Add(options.Project.Stack);
        values.Add(options.Validation.Flutter.ProjectPath);
        values.Add(options.Validation.Flutter.DeviceProbeCommand);
        values.Add(options.Qa.ProjectPath);
        values.Add(options.Qa.PatrolCommand);
        values.Add(options.Qa.AdbCommand);
        values.Add(options.Qa.Device);
        values.Add(options.Reference.RequiredBranch);
        values.AddRange(options.Git.ProtectedBranches);

        var found = values.Where(value => ForbiddenTokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return found.Length == 0
            ? []
            : [$"Generic project profile contains OnlineOS-specific configuration: {string.Join(", ", found)}."];
    }
}
