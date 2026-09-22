using OnlineOs.AiOrchestrator.Infrastructure;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task CapturesFailedProcessExecution()
    {
        var result = await new ProcessRunner().RunAsync(new ProcessSpec("/usr/bin/false", [], Directory.GetCurrentDirectory()));
        Assert.False(result.Succeeded);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task TerminatesTimedOutProcessTree()
    {
        var result = await new ProcessRunner().RunAsync(new ProcessSpec(
            "/bin/sh", ["-c", "sleep 5"], Directory.GetCurrentDirectory(), Timeout: TimeSpan.FromMilliseconds(50)));

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
    }
}
