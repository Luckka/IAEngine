using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed class ValidationRunner(IProcessRunner processes, ValidationOptions options, string repository, TimeSpan timeout) : IValidationRunner
{
    public async Task<IReadOnlyList<ValidationResult>> RunAsync(CancellationToken ct = default)
        => await RunCommandsAsync(options.Commands.Where(x => x.Enabled), ct);

    public async Task<IReadOnlyList<ValidationResult>> RunAsync(EngineeringProfile engineering, CancellationToken ct = default)
    {
        if (!engineering.IsFlutterTask)
            return await RunCommandsAsync(options.Commands.Where(x => x.Enabled && !IsFlutterCategory(EffectiveCategory(x))), ct);

        var expected = engineering.ExpectedValidation
            .Where(x => x.Applicability != StandardApplicability.NotApplicable)
            .ToDictionary(x => x.Category, StringComparer.OrdinalIgnoreCase);
        var configured = options.Commands
            .Where(x => x.Enabled)
            .GroupBy(EffectiveCategory, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var results = new List<ValidationResult>();
        var orderedExpectations = options.Commands
            .Where(x => x.Enabled)
            .Select(EffectiveCategory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(expected.ContainsKey)
            .Select(category => expected[category])
            .Concat(expected.Values.Where(x => !configured.ContainsKey(x.Category)));
        foreach (var expectation in orderedExpectations)
        {
            if (!configured.TryGetValue(expectation.Category, out var command))
            {
                if (expectation.Applicability != StandardApplicability.Required) continue;
                results.Add(ConfigurationFailure(expectation.Category, true, $"Required validation category is not configured: {expectation.Category}."));
                continue;
            }
            var required = expectation.Applicability == StandardApplicability.Required;
            results.Add(expectation.Category.Equals("IntegrationTests", StringComparison.OrdinalIgnoreCase)
                ? await RunIntegrationAsync(command, required, ct)
                : await RunCommandAsync(command, required, ct));
        }
        return results;
    }

    private async Task<IReadOnlyList<ValidationResult>> RunCommandsAsync(IEnumerable<ValidationCommandOptions> commands, CancellationToken ct)
    {
        var results = new List<ValidationResult>();
        foreach (var command in commands)
            results.Add(await RunCommandAsync(command, command.Required, ct));
        return results;
    }

    private async Task<ValidationResult> RunCommandAsync(ValidationCommandOptions command, bool required, CancellationToken ct)
    {
        var category = EffectiveCategory(command);
        var workingDirectory = ResolveWorkingDirectory(command);
        if (workingDirectory is null)
            return ConfigurationFailure(category, required, $"Validation working directory escapes the OnlineOS workspace: {command.WorkingDirectory}.");
        if (string.IsNullOrWhiteSpace(command.Command))
            return ConfigurationFailure(category, required, $"Validation command is empty for category {category}.");
        var process = await processes.RunAsync(new ProcessSpec(command.Command, command.Arguments, workingDirectory, Timeout: timeout), ct);
        return new ValidationResult(category, required, process, process.Succeeded ? ValidationStatus.Pass : ValidationStatus.Fail);
    }

    private async Task<ValidationResult> RunIntegrationAsync(ValidationCommandOptions command, bool required, CancellationToken ct)
    {
        var projectPath = ResolvePath(options.Flutter.ProjectPath);
        if (projectPath is null)
            return ConfigurationFailure("IntegrationTests", required, "Flutter project path escapes the OnlineOS workspace.");
        if (!Directory.Exists(Path.Combine(projectPath, "integration_test")))
            return new ValidationResult("IntegrationTests", required, SyntheticProcess("integration tests", 0), ValidationStatus.NotApplicable, "No integration_test directory exists.");

        var probe = await processes.RunAsync(new ProcessSpec(options.Flutter.DeviceProbeCommand, options.Flutter.DeviceProbeArguments, projectPath, Timeout: timeout), ct);
        if (!probe.Succeeded || !HasTargetDevice(probe.StandardOutput))
        {
            var reason = !probe.Succeeded
                ? $"Could not enumerate a supported Flutter integration target: {probe.StandardError.Trim()}"
                : $"No supported Flutter integration target was found{(string.IsNullOrWhiteSpace(options.Flutter.IntegrationDevice) ? "." : $" for '{options.Flutter.IntegrationDevice}'.")}";
            return new ValidationResult("IntegrationTests", required, new ProcessResult(probe.Command, probe.ExitCode, probe.StandardOutput, probe.StandardError, probe.Duration, probe.TimedOut), ValidationStatus.NotExecutable, reason);
        }
        return await RunCommandAsync(command, required, ct);
    }

    private bool HasTargetDevice(string output) => string.IsNullOrWhiteSpace(options.Flutter.IntegrationDevice)
        ? !string.IsNullOrWhiteSpace(output)
        : output.Contains(options.Flutter.IntegrationDevice, StringComparison.OrdinalIgnoreCase);

    private string? ResolveWorkingDirectory(ValidationCommandOptions command) => ResolvePath(command.WorkingDirectory ?? (EffectiveCategory(command) is "Format" or "StaticAnalysis" or "UnitTests" or "ResponsiveWidgetTests" or "IntegrationTests" ? options.Flutter.ProjectPath : "."));

    private string? ResolvePath(string path)
    {
        var boundary = new WorkspaceBoundary(repository);
        try { return boundary.Resolve(path); }
        catch (InvalidOperationException) { return null; }
    }

    private static string EffectiveCategory(ValidationCommandOptions command) => string.IsNullOrWhiteSpace(command.Category) ? command.Name : command.Category;
    private static bool IsFlutterCategory(string category) => category is "Format" or "StaticAnalysis" or "UnitTests" or "ResponsiveWidgetTests" or "IntegrationTests";
    private static ValidationResult ConfigurationFailure(string category, bool required, string reason) => new(category, required, SyntheticProcess("configuration", -1, reason), ValidationStatus.ConfigurationError, reason);
    private static ProcessResult SyntheticProcess(string command, int exitCode, string? error = null) => new(command, exitCode, "", error ?? "", TimeSpan.Zero);
}
