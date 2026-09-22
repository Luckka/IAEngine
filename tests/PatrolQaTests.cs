using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class PatrolQaTests
{
    [Fact]
    public void FastCustomerTaskSelectsFocusedJourney()
    {
        var selected = PatrolTestSelector.Select(E2EProfile.Fast, "Fix customer summary layout");
        Assert.Single(selected);
        Assert.Contains("customer_summary_test.dart", selected[0]);
    }

    [Fact]
    public void MilestoneSelectsBroaderJourneySetThanFast()
    {
        var fast = PatrolTestSelector.Select(E2EProfile.Fast, "unrelated refactor");
        var milestone = PatrolTestSelector.Select(E2EProfile.Milestone);
        Assert.True(milestone.Count > fast.Count);
    }

    [Fact]
    public void NormalizesAbsoluteTargetToAppRelativePath()
    {
        var repository = Path.Combine(Path.GetTempPath(), "parent with spaces", "ONLINEOS-MOBILE", "onlineos-mobile");
        var target = Path.Combine(repository, "app", "integration_test", "smoke", "technician_journey_test.dart");

        var normalized = PatrolE2ETestRunner.NormalizeTargets([target], repository, "app");

        Assert.Equal("integration_test/smoke/technician_journey_test.dart", normalized.Single());
        Assert.DoesNotContain("ONLINEOS-MOBILE", normalized.Single());
        Assert.DoesNotContain(" ", normalized.Single());
    }

    [Fact]
    public void KeepsRelativeTargetAndNormalizesSeparators()
    {
        var normalized = PatrolE2ETestRunner.NormalizeTargets(["integration_test/smoke/technician_journey_test.dart"], "/repo-with-hyphen", "app");

        Assert.Equal("integration_test/smoke/technician_journey_test.dart", normalized.Single());
    }

    [Fact]
    public void RejectsTargetOutsideFlutterAppRoot()
    {
        var exception = Assert.Throws<ArgumentException>(() => PatrolE2ETestRunner.NormalizeTargets(["../outside_test.dart"], "/repo with spaces", "app"));

        Assert.Contains("outside the Flutter app root", exception.Message);
    }

    [Fact]
    public async Task ReportReferencesEvidenceAndStatus()
    {
        var directory = Path.Combine(Path.GetTempPath(), "onlineos-qa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "screenshots"));
        var image = Path.Combine(directory, "screenshots", "agenda.png");
        await File.WriteAllBytesAsync(image, [137, 80, 78, 71]);
        var request = new E2ETestRequest("QA-TEST", "TASK-1", "M2", E2EProfile.Fast);
        var result = new E2ETestResult(true, 0, TimeSpan.FromSeconds(1), request, ["patrol_test/smoke/technician_journey_test.dart"], [image], [], "stdout.log", "stderr.log", "result.json", "report.html");
        var report = await QaReportWriter.WriteAsync(directory, result);
        var html = await File.ReadAllTextAsync(report);
        Assert.Contains("QA-TEST", html);
        Assert.Contains("agenda.png", html);
        Assert.Contains("PASS", html);
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task ReusesAnyReadyEmulatorRegardlessOfConfiguredAvd()
    {
        var processes = new FakeProcessRunner(call => call.Arguments.SequenceEqual(["devices"]) ? new ProcessResult("adb", 0, "List of devices attached\nemulator-5554\tdevice\n", "", TimeSpan.Zero)
            : call.Arguments.Contains("getprop") ? new ProcessResult("adb", 0, "1\n", "", TimeSpan.Zero)
            : new ProcessResult("adb", 0, "online\n", "", TimeSpan.Zero));
        var result = await new PatrolDeviceManager(processes, new QaOptions { EmulatorAvd = "OnlineOS-QA" }, Directory.GetCurrentDirectory())
            .EnsureReadyAsync(new E2ETestRequest("QA-1", null, null, E2EProfile.Fast));

        Assert.True(result.Ready);
        Assert.True(result.Reused);
        Assert.Equal("emulator-5554", result.Device);
        Assert.Equal(3, processes.Calls.Count);
        Assert.Equal(["devices"], processes.Calls[0].Arguments);
    }

    [Theory]
    [InlineData("emulator-5554\toffline")]
    [InlineData("emulator-5554\tunauthorized")]
    public async Task DoesNotReuseUnusableEmulatorStates(string deviceLine)
    {
        var processes = new FakeProcessRunner(call => call.Arguments.SequenceEqual(["devices"]) ? new ProcessResult("adb", 0, $"List of devices attached\n{deviceLine}\n", "", TimeSpan.Zero)
            : call.Arguments.SequenceEqual(["-list-avds"]) ? new ProcessResult("emulator", 0, "OnlineOS-QA\n", "", TimeSpan.Zero)
            : new ProcessResult("emulator", 0, "", "", TimeSpan.Zero));
        var result = await new PatrolDeviceManager(processes, new QaOptions { EmulatorAvd = "OnlineOS-QA", DeviceReadyTimeoutSeconds = 0 }, Directory.GetCurrentDirectory())
            .EnsureReadyAsync(new E2ETestRequest("QA-2", null, null, E2EProfile.Fast));

        Assert.False(result.Ready);
        Assert.Equal(E2EFailureCategory.EmulatorBootTimeout, result.FailureCategory);
        Assert.Contains(processes.Calls, call => call.Arguments.SequenceEqual(["-avd", "OnlineOS-QA", "-no-snapshot-save"]));
    }

    [Fact]
    public async Task StartsConfiguredAvdWhenNoUsableEmulatorExists()
    {
        var processes = new FakeProcessRunner(call => call.Arguments.SequenceEqual(["devices"])
            ? new ProcessResult("adb devices", 0, "List of devices attached\n", "", TimeSpan.Zero)
            : call.Arguments.SequenceEqual(["-list-avds"]) ? new ProcessResult("emulator", 0, "OnlineOS-QA\n", "", TimeSpan.Zero)
            : new ProcessResult("emulator", 0, "", "", TimeSpan.Zero));
        var result = await new PatrolDeviceManager(processes, new QaOptions { EmulatorAvd = "OnlineOS-QA", DeviceReadyTimeoutSeconds = 0 }, Directory.GetCurrentDirectory())
            .EnsureReadyAsync(new E2ETestRequest("QA-3", null, null, E2EProfile.Fast));

        Assert.False(result.Ready);
        Assert.Contains(processes.Calls, call => call.Arguments.SequenceEqual(["-avd", "OnlineOS-QA", "-no-snapshot-save"]));
    }

    [Fact]
    public async Task PropagatesSelectedSerialToPatrolRequestAndReport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "onlineos-qa-" + Guid.NewGuid().ToString("N"));
        var processes = new FakeProcessRunner(call => call.Arguments.SequenceEqual(["devices"])
            ? new ProcessResult("adb", 0, "List of devices attached\nemulator-5554\tdevice\n", "", TimeSpan.Zero)
            : call.Arguments.Contains("getprop") ? new ProcessResult("adb", 0, "1\n", "", TimeSpan.Zero)
            : call.Arguments.Contains("echo") ? new ProcessResult("adb", 0, "online\n", "", TimeSpan.Zero)
            : new ProcessResult("patrol", 0, "smoke passed", "", TimeSpan.Zero));
        var options = new QaOptions { ProjectPath = ".", PatrolCommand = "/bin/sh", AdbCommand = "adb", EmulatorCommand = "emulator" };
        var request = new E2ETestRequest("QA-4", null, null, E2EProfile.Fast, Device: "OnlineOS-QA", ArtifactDirectory: directory);

        var result = await new PatrolE2ETestRunner(processes, new PatrolDeviceManager(processes, options, Directory.GetCurrentDirectory()), options, Directory.GetCurrentDirectory()).RunAsync(request);

        Assert.True(result.Success);
        Assert.Equal("emulator-5554", result.Request.Device);
        Assert.Contains(processes.Calls, call => call.Arguments.Contains("emulator-5554"));
        Assert.DoesNotContain(processes.Calls, call => call.Arguments.Contains("OnlineOS-QA"));
        var patrolCall = processes.Calls.Single(call => call.Arguments.Count > 0 && call.Arguments[0] == "test");
        Assert.Equal("/bin/sh", patrolCall.FileName);
        Assert.Equal(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), options.ProjectPath)), patrolCall.WorkingDirectory);
        Assert.Equal(["test", "--device", "emulator-5554", "--dart-define", "ONLINEOS_QA_RUN_ID=QA-4", "--target", "integration_test/smoke/technician_journey_test.dart"], patrolCall.Arguments);
        Assert.Contains("emulator-5554", await File.ReadAllTextAsync(result.ReportPath));
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void ResolvesConfiguredPatrolPathBeforePathLookup()
    {
        var root = CreateTempDirectory();
        var executable = Path.Combine(root, "patrol");
        File.WriteAllText(executable, "");
        var resolution = new PatrolExecutableResolver(new QaOptions { PatrolCommand = executable }, root, path: "") .Resolve();

        Assert.True(resolution.Available);
        Assert.Equal(Path.GetFullPath(executable), resolution.ExecutablePath);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void ResolvesPatrolFromPubCacheAndIgnoresPathDifference()
    {
        var root = CreateTempDirectory();
        var pubCache = Path.Combine(root, "pub-cache");
        var executable = Path.Combine(pubCache, "bin", "patrol");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "");
        var previous = Environment.GetEnvironmentVariable("PUB_CACHE");
        try
        {
            Environment.SetEnvironmentVariable("PUB_CACHE", pubCache);
            var resolution = new PatrolExecutableResolver(new QaOptions { PatrolCommand = "patrol" }, root, path: "/different/path", shellLookup: _ => null).Resolve();
            Assert.Equal(Path.GetFullPath(executable), resolution.ExecutablePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PUB_CACHE", previous);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MissingPatrolIsInfrastructureFailureAndDoesNotStartProcess()
    {
        var root = CreateTempDirectory();
        var processes = new FakeProcessRunner(_ => throw new Xunit.Sdk.XunitException("Patrol should not start"));
        var options = new QaOptions { ProjectPath = ".", PatrolCommand = Path.Combine(root, "missing-patrol") };
        var result = await new PatrolE2ETestRunner(processes, new FakeReadyDeviceManager(), options, root).RunAsync(new E2ETestRequest("QA-MISSING", null, null, E2EProfile.Fast, ArtifactDirectory: Path.Combine(root, "artifacts")));

        Assert.Equal(E2EFailureCategory.PatrolCliUnavailable, result.FailureCategory);
        Assert.Empty(processes.Calls);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task MissingAppIsConfigurationFailureAndDoesNotStartProcess()
    {
        var root = CreateTempDirectory();
        var processes = new FakeProcessRunner(_ => throw new Xunit.Sdk.XunitException("Patrol should not start"));
        var result = await new PatrolE2ETestRunner(processes, new FakeReadyDeviceManager(), new QaOptions { ProjectPath = "missing-app", PatrolCommand = "/bin/sh" }, root)
            .RunAsync(new E2ETestRequest("QA-NO-APP", null, null, E2EProfile.Fast, ArtifactDirectory: Path.Combine(root, "artifacts")));

        Assert.Equal(E2EFailureCategory.QaConfigurationFailure, result.FailureCategory);
        Assert.Empty(processes.Calls);
        Directory.Delete(root, recursive: true);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "onlineos-patrol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeReadyDeviceManager : IE2EDeviceManager
    {
        public Task<E2EDeviceResult> EnsureReadyAsync(E2ETestRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new E2EDeviceResult(true, true, "emulator-5554"));
    }

    private sealed class FakeProcessRunner(Func<ProcessSpec, ProcessResult> handler) : IProcessRunner
    {
        public List<ProcessSpec> Calls { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Calls.Add(spec);
            return Task.FromResult(handler(spec));
        }
        public Task<ProcessResult> StartDetachedAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Calls.Add(spec);
            return Task.FromResult(handler(spec));
        }
    }
}
