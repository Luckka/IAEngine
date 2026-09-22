using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class ValidationRunnerTests
{
    [Fact]
    public async Task FlutterProfileRunsEachConfiguredCategoryFromAppRoot()
    {
        var process = new FakeProcessRunner();
        var runner = new ValidationRunner(process, Options(includeIntegration: true), RepositoryRoot(), TimeSpan.FromSeconds(5));

        var results = await runner.RunAsync(FlutterProfile());

        Assert.Equal(["Format", "StaticAnalysis", "UnitTests", "ResponsiveWidgetTests", "IntegrationTests"], results.Select(x => x.Category));
        Assert.All(results.Where(x => x.Category != "IntegrationTests"), x => Assert.True(x.Passed));
        Assert.True(Assert.Single(results, x => x.Category == "IntegrationTests").Passed);
        Assert.All(process.Specs.Where(x => x.FileName is "dart" or "flutter"), x => Assert.Equal(Path.Combine(RepositoryRoot(), "app"), x.WorkingDirectory));
    }

    [Fact]
    public async Task MissingRequiredCategoryProducesConfigurationFailure()
    {
        var options = Options(includeIntegration: false);
        options.Commands.RemoveAll(x => x.Category == "ResponsiveWidgetTests");
        var results = await new ValidationRunner(new FakeProcessRunner(), options, RepositoryRoot(), TimeSpan.FromSeconds(5)).RunAsync(FlutterProfile());

        var result = Assert.Single(results, x => x.Category == "ResponsiveWidgetTests");
        Assert.Equal(ValidationStatus.ConfigurationError, result.Status);
        Assert.False(result.Passed);
        Assert.Contains("not configured", result.Reason);
    }

    [Fact]
    public async Task NonZeroAndTimeoutAreFailuresWithProcessMetadata()
    {
        var process = new FakeProcessRunner { ExitCode = 2, TimedOut = true };
        var options = new ValidationOptions { Commands = [new() { Category = "UnitTests", Command = "flutter", Arguments = ["test"], WorkingDirectory = "app" }] };
        var result = Assert.Single(await new ValidationRunner(process, options, RepositoryRoot(), TimeSpan.FromSeconds(5)).RunAsync(FlutterProfile()), x => x.Category == "UnitTests");

        Assert.Equal(2, result.Process.ExitCode);
        Assert.True(result.Process.TimedOut);
        Assert.Equal(ValidationStatus.Fail, result.Status);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task DocumentationDoesNotRunFlutterCommands()
    {
        var process = new FakeProcessRunner();
        var options = Options(includeIntegration: true);
        var documentation = EngineeringStandardsPolicy.Create(
            new DevelopmentTask("DOC", "Document setup", "Document setup", "documentation"),
            new RoutingResult("documentation", ["documentation"], "low", "low", [], [], "docs", "docs"));

        var results = await new ValidationRunner(process, options, RepositoryRoot(), TimeSpan.FromSeconds(5)).RunAsync(documentation);

        Assert.Empty(results);
        Assert.Empty(process.Specs);
    }

    [Fact]
    public async Task IntegrationWithoutTargetIsExplicitlyNotExecutable()
    {
        var process = new FakeProcessRunner { DeviceOutput = "[]" };
        var options = Options(includeIntegration: true);
        var result = Assert.Single(await new ValidationRunner(process, options, RepositoryRoot(), TimeSpan.FromSeconds(5)).RunAsync(FlutterProfile()), x => x.Category == "IntegrationTests");

        Assert.Equal(ValidationStatus.NotExecutable, result.Status);
        Assert.False(result.Passed);
        Assert.Contains("No supported", result.Reason);
    }

    [Fact]
    public async Task ConfiguredIntegrationTestsRequiredFalseCannotWeakenARequiredProfileCategory()
    {
        var process = new FakeProcessRunner { ExitCode = 1 };
        var options = Options(includeIntegration: false);
        options.Commands.Add(new() { Category = "IntegrationTests", Command = "flutter", Arguments = ["test", "integration_test"], WorkingDirectory = "app", Required = false });

        var result = Assert.Single(await new ValidationRunner(process, options, RepositoryRoot(), TimeSpan.FromSeconds(5)).RunAsync(FlutterProfile()), x => x.Category == "IntegrationTests");

        Assert.True(result.Required);
        Assert.False(result.Passed);
    }

    private static EngineeringProfile FlutterProfile() => EngineeringStandardsPolicy.Create(
        new DevelopmentTask("M1", "Implement Flutter UI foundation", "Implement Flutter responsive foundation", "feature", ["ui", "database"]),
        new RoutingResult("feature", ["ui", "database"], "high", "high", ["flutter"], [], "claude-codex", "foundation"));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static ValidationOptions Options(bool includeIntegration) => new()
    {
        Flutter = new FlutterValidationOptions(),
        Commands = BuildCommands(includeIntegration)
    };

    private static List<ValidationCommandOptions> BuildCommands(bool includeIntegration)
    {
        var commands = new List<ValidationCommandOptions>
        {
            new() { Category = "Format", Command = "dart", Arguments = ["format"], WorkingDirectory = "app" },
            new() { Category = "StaticAnalysis", Command = "flutter", Arguments = ["analyze"], WorkingDirectory = "app" },
            new() { Category = "UnitTests", Command = "flutter", Arguments = ["test"], WorkingDirectory = "app" },
            new() { Category = "ResponsiveWidgetTests", Command = "flutter", Arguments = ["test", "responsive"], WorkingDirectory = "app" }
        };
        if (includeIntegration) commands.Add(new() { Category = "IntegrationTests", Command = "flutter", Arguments = ["test", "integration_test"], WorkingDirectory = "app" });
        return commands;
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<ProcessSpec> Specs { get; } = [];
        public int ExitCode { get; init; }
        public bool TimedOut { get; init; }
        public string DeviceOutput { get; init; } = "[{\"id\":\"macos\"}]";
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Specs.Add(spec);
            var isProbe = spec.Arguments.SequenceEqual(["devices", "--machine"]);
            return Task.FromResult(new ProcessResult(string.Join(' ', new[] { spec.FileName }.Concat(spec.Arguments)), isProbe ? 0 : ExitCode, isProbe ? DeviceOutput : "", "", TimeSpan.FromMilliseconds(1), isProbe ? false : TimedOut));
        }
    }
}
