using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed class PatrolDeviceManager(IProcessRunner processes, QaOptions options, string repository) : IE2EDeviceManager
{
    private static readonly SemaphoreSlim StartupGate = new(1, 1);

    public async Task<E2EDeviceResult> EnsureReadyAsync(E2ETestRequest request, CancellationToken ct = default)
    {
        if (!string.Equals(request.Platform, "android", StringComparison.OrdinalIgnoreCase))
            return new(false, false, request.Device, E2EFailureCategory.ConfigurationFailure, "Only Android is enabled in the first Patrol integration.");

        var adb = ResolveAdbCommand(options);
        var existing = await FindReadyDeviceAsync(adb, ct);
        if (existing is not null) return new(true, true, existing, Detail: "Reused running Android emulator.");

        await StartupGate.WaitAsync(ct);
        try
        {
            existing = await FindReadyDeviceAsync(adb, ct);
            if (existing is not null) return new(true, true, existing, Detail: "Reused running Android emulator after startup synchronization.");

            var emulator = ResolveEmulatorCommand(options);
            var avds = await processes.RunAsync(new ProcessSpec(emulator, ["-list-avds"], repository, Timeout: TimeSpan.FromSeconds(10)), ct);
            if (!avds.Succeeded) return new(false, false, request.Device, E2EFailureCategory.EmulatorUnavailable, avds.StandardError);
            var available = avds.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            var avd = SelectAvd(available);
            if (avd is null) return new(false, false, request.Device, E2EFailureCategory.EmulatorUnavailable, "No configured QA AVD is available.");

            var start = await processes.StartDetachedAsync(new ProcessSpec(emulator, ["-avd", avd, "-no-snapshot-save"], repository), ct);
            if (!start.Succeeded) return new(false, false, request.Device, E2EFailureCategory.EmulatorStartupFailure, start.StandardError);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(options.DeviceReadyTimeoutSeconds);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
                existing = await FindReadyDeviceAsync(adb, ct);
                if (existing is not null) return new(true, false, existing, Detail: $"Started AVD '{avd}' and verified Android boot readiness.");
            }
            return new(false, false, request.Device, E2EFailureCategory.EmulatorBootTimeout, $"Emulator did not finish booting within {options.DeviceReadyTimeoutSeconds}s.");
        }
        finally { StartupGate.Release(); }
    }

    private async Task<string?> FindReadyDeviceAsync(string adb, CancellationToken ct)
    {
        var probe = await processes.RunAsync(new ProcessSpec(adb, ["devices"], repository, Timeout: TimeSpan.FromSeconds(10)), ct);
        var serial = FindUsableEmulator(probe);
        if (serial is null) return null;
        var boot = await processes.RunAsync(new ProcessSpec(adb, ["-s", serial, "shell", "getprop", "sys.boot_completed"], repository, Timeout: TimeSpan.FromSeconds(10)), ct);
        if (!boot.Succeeded || !string.Equals(boot.StandardOutput.Trim(), "1", StringComparison.Ordinal)) return null;
        var usable = await processes.RunAsync(new ProcessSpec(adb, ["-s", serial, "shell", "echo", "online"], repository, Timeout: TimeSpan.FromSeconds(10)), ct);
        return usable.Succeeded && usable.StandardOutput.Trim() == "online" ? serial : null;
    }

    private string? SelectAvd(IReadOnlyList<string> available)
    {
        if (!string.IsNullOrWhiteSpace(options.EmulatorAvd) && available.Contains(options.EmulatorAvd, StringComparer.Ordinal)) return options.EmulatorAvd;
        if (!string.IsNullOrWhiteSpace(options.EmulatorFallbackAvd) && available.Contains(options.EmulatorFallbackAvd, StringComparer.Ordinal)) return options.EmulatorFallbackAvd;
        return null;
    }

    public static string? FindUsableEmulator(ProcessResult probe)
    {
        if (!probe.Succeeded) return null;
        foreach (var line in probe.StandardOutput.Split('\n'))
        {
            var fields = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 && fields[0].StartsWith("emulator-", StringComparison.OrdinalIgnoreCase) && fields[1] == "device") return fields[0];
        }
        return null;
    }

    public static string ResolveAdbCommand(QaOptions options) => ResolveTool(options.AdbCommand, "platform-tools");
    public static string ResolveEmulatorCommand(QaOptions options) => ResolveTool(options.EmulatorCommand, "emulator");

    private static string ResolveTool(string configured, string sdkDirectory)
    {
        var roots = new[] { Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"), Environment.GetEnvironmentVariable("ANDROID_HOME"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Android", "sdk") };
        foreach (var root in roots.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var candidate = Path.Combine(root!, sdkDirectory, configured);
            if (File.Exists(candidate)) return candidate;
        }
        if (Path.IsPathRooted(configured) && File.Exists(configured)) return configured;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, configured);
            if (File.Exists(candidate)) return candidate;
        }
        return configured;
    }
}
