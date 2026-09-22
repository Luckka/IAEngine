using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class PatrolArtifactTests
{
    [Fact]
    public async Task CheckpointMarkersCaptureMultipleScreenshotsOnSelectedDevice()
    {
        var directory = Path.Combine(Path.GetTempPath(), "onlineos-artifacts-" + Guid.NewGuid().ToString("N"));
        var processes = new FakeProcessRunner(["noise", "ONLINEOS_QA_CHECKPOINT|foundation/home", "ONLINEOS_QA_CHECKPOINT|agenda"]);
        var result = await CreateRunner(processes, directory).RunAsync(new E2ETestRequest("QA-A", null, null, E2EProfile.Fast, ArtifactDirectory: directory));

        Assert.True(result.Success);
        Assert.Equal(2, result.Screenshots.Length);
        Assert.All(result.Screenshots, path => Assert.StartsWith(Path.Combine(directory, "screenshots"), path));
        Assert.Contains("foundation-home.png", result.Screenshots.Single(path => path.Contains("foundation-home")));
        Assert.Contains("agenda.png", await File.ReadAllTextAsync(result.ReportPath));
        Assert.All(processes.Calls.Where(call => call.StandardOutputFile is not null), call => Assert.Equal("emulator-5554", call.Arguments[1]));
        Assert.Equal(3, processes.Calls.Count);
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task ScreenshotFailureIsVisualEvidenceFailureNotProductFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "onlineos-artifacts-" + Guid.NewGuid().ToString("N"));
        var processes = new FakeProcessRunner(["ONLINEOS_QA_CHECKPOINT|agenda"], failArtifactCapture: true);
        var result = await CreateRunner(processes, directory).RunAsync(new E2ETestRequest("QA-B", null, null, E2EProfile.Fast, ArtifactDirectory: directory));

        Assert.True(result.Success);
        Assert.Empty(result.Screenshots);
        Assert.Contains("Missing screenshot", await File.ReadAllTextAsync(result.ReportPath));
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task NormalPatrolOutputDoesNotTriggerCapture()
    {
        var processes = new FakeProcessRunner(["QA screenshot checkpoint: agenda"]);
        var directory = Path.Combine(Path.GetTempPath(), "onlineos-artifacts-" + Guid.NewGuid().ToString("N"));
        var result = await CreateRunner(processes, directory).RunAsync(new E2ETestRequest("QA-C", null, null, E2EProfile.Fast, ArtifactDirectory: directory));

        Assert.True(result.Success);
        Assert.Empty(result.Screenshots);
        Assert.Single(processes.Calls);
        Directory.Delete(directory, recursive: true);
    }

    [Theory]
    [InlineData("agenda/home", "agenda-home")]
    [InlineData("../outside", "outside")]
    [InlineData("customer summary", "customer-summary")]
    public void ScreenshotNamesAreSafe(string input, string expected)
        => Assert.Equal(expected, AdbDeviceArtifactCapture.Sanitize(input));

    private static PatrolE2ETestRunner CreateRunner(FakeProcessRunner processes, string directory)
    {
        var options = new QaOptions { ProjectPath = ".", PatrolCommand = "patrol" };
        return new PatrolE2ETestRunner(processes, new ReadyDeviceManager(), options, Directory.GetCurrentDirectory(),
            new AdbDeviceArtifactCapture(processes, options, Directory.GetCurrentDirectory(), enableMonitor: false));
    }

    private sealed class ReadyDeviceManager : IE2EDeviceManager
    {
        public Task<E2EDeviceResult> EnsureReadyAsync(E2ETestRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new E2EDeviceResult(true, true, "emulator-5554"));
    }

    private sealed class FakeProcessRunner(IReadOnlyList<string> output, bool failArtifactCapture = false) : IProcessRunner
    {
        public List<ProcessSpec> Calls { get; } = [];
        public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Calls.Add(spec);
            if (spec.StandardOutputFile is not null)
            {
                if (failArtifactCapture) return new ProcessResult("adb", 1, "", "capture failed", TimeSpan.Zero);
                await File.WriteAllBytesAsync(spec.StandardOutputFile, [137, 80, 78, 71], cancellationToken);
                return new ProcessResult("adb", 0, "", "", TimeSpan.Zero);
            }
            if (spec.StandardOutputLineHandler is not null)
                foreach (var line in output) await spec.StandardOutputLineHandler(line);
            return new ProcessResult("patrol", 0, string.Join('\n', output), "", TimeSpan.Zero);
        }
    }
}
