using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Tests;

public sealed class PatrolDeviceLifecycleTests
{
    [Fact]
    public async Task StartsConfiguredAvdAndWaitsForBootAndUsability()
    {
        var runner = new SequenceRunner(
            Devices(), Devices(), Avds("OnlineOS-QA"), Start(), Devices("emulator-5554"), Boot("1"), Usable());
        var result = await Manager(runner, new QaOptions { EmulatorAvd = "OnlineOS-QA", DeviceReadyTimeoutSeconds = 2 }).EnsureReadyAsync(Request());

        Assert.True(result.Ready);
        Assert.False(result.Reused);
        Assert.Equal("emulator-5554", result.Device);
        Assert.Single(runner.Detached);
        Assert.Equal(["-avd", "OnlineOS-QA", "-no-snapshot-save"], runner.Detached[0].Arguments);
        Assert.Contains(runner.Calls, call => call.Arguments.SequenceEqual(["-s", "emulator-5554", "shell", "getprop", "sys.boot_completed"]));
        Assert.Contains(runner.Calls, call => call.Arguments.SequenceEqual(["-s", "emulator-5554", "shell", "echo", "online"]));
    }

    [Fact]
    public async Task BootTimeoutIsDistinctFromMissingAvd()
    {
        var runner = new SequenceRunner(Devices(), Devices(), Avds("OnlineOS-QA"), Start(), Devices("emulator-5554"), Boot("0"));
        var result = await Manager(runner, new QaOptions { EmulatorAvd = "OnlineOS-QA", DeviceReadyTimeoutSeconds = 1 }).EnsureReadyAsync(Request());
        Assert.Equal(E2EFailureCategory.EmulatorBootTimeout, result.FailureCategory);
        Assert.Single(runner.Detached);

        var missing = new SequenceRunner(Devices(), Devices(), Avds("Other"));
        var missingResult = await Manager(missing, new QaOptions { EmulatorAvd = "OnlineOS-QA" }).EnsureReadyAsync(Request());
        Assert.Equal(E2EFailureCategory.EmulatorUnavailable, missingResult.FailureCategory);
        Assert.Empty(missing.Detached);
    }

    [Fact]
    public async Task ConcurrentRunsStartAtMostOneEmulator()
    {
        var runner = new ConcurrentRunner();
        var manager = Manager(runner, new QaOptions { EmulatorAvd = "OnlineOS-QA", DeviceReadyTimeoutSeconds = 0 });
        var results = await Task.WhenAll(manager.EnsureReadyAsync(Request()), manager.EnsureReadyAsync(Request()));

        Assert.Equal(1, runner.StartCount);
        Assert.Contains(results, result => result.Ready && result.Device == "emulator-5554");
    }

    private static PatrolDeviceManager Manager(IProcessRunner runner, QaOptions options) => new(runner, options, Directory.GetCurrentDirectory());
    private static E2ETestRequest Request() => new("QA-LIFECYCLE", null, null, E2EProfile.Fast);
    private static ProcessResult Devices(string? serial = null) => new("adb devices", 0, serial is null ? "List of devices attached\n" : $"List of devices attached\n{serial}\tdevice\n", "", TimeSpan.Zero);
    private static ProcessResult Avds(params string[] names) => new("emulator -list-avds", 0, string.Join('\n', names), "", TimeSpan.Zero);
    private static ProcessResult Start() => new("emulator", 0, "", "", TimeSpan.Zero);
    private static ProcessResult Boot(string value) => new("adb boot", 0, value, "", TimeSpan.Zero);
    private static ProcessResult Usable() => new("adb shell", 0, "online", "", TimeSpan.Zero);

    private sealed class SequenceRunner(params ProcessResult[] results) : IProcessRunner
    {
        private int index;
        public List<ProcessSpec> Calls { get; } = [];
        public List<ProcessSpec> Detached { get; } = [];
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Calls.Add(spec);
            return Task.FromResult(results[Math.Min(index++, results.Length - 1)]);
        }
        public Task<ProcessResult> StartDetachedAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Detached.Add(spec);
            return Task.FromResult(Start());
        }
    }

    private sealed class ConcurrentRunner : IProcessRunner
    {
        public int StartCount;
        private volatile bool started;
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        {
            if (spec.Arguments.SequenceEqual(["devices"])) return Task.FromResult(Devices(started ? "emulator-5554" : null));
            if (spec.Arguments.Contains("getprop")) return Task.FromResult(Boot("1"));
            if (spec.Arguments.Contains("echo")) return Task.FromResult(Usable());
            return Task.FromResult(Avds("OnlineOS-QA"));
        }
        public Task<ProcessResult> StartDetachedAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref StartCount);
            started = true;
            return Task.FromResult(Start());
        }
    }
}
