using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using System.Diagnostics;

namespace OnlineOs.AiOrchestrator.Pipeline;

public interface IDeviceArtifactCapture
{
    Task<ScreenshotCaptureResult> CaptureScreenshotAsync(string serial, string name, string directory, CancellationToken ct = default);
}

public sealed class AdbDeviceArtifactCapture(IProcessRunner processes, QaOptions options, string repository, bool enableMonitor = true) : IDeviceArtifactCapture
{
    private Process? monitor;
    private Task? monitorTask;
    public async Task<ScreenshotCaptureResult> CaptureScreenshotAsync(string serial, string name, string directory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var safeName = Sanitize(name);
        var path = Path.Combine(directory, safeName + ".png");
        var adb = PatrolDeviceManager.ResolveAdbCommand(options);
        var result = await processes.RunAsync(new ProcessSpec(adb, ["-s", serial, "exec-out", "screencap", "-p"], repository,
            Timeout: TimeSpan.FromSeconds(10), StandardOutputFile: path), ct);
        if (!result.Succeeded || !File.Exists(path) || new FileInfo(path).Length == 0)
        {
            var category = result.StandardError.Contains("device", StringComparison.OrdinalIgnoreCase)
                ? E2EFailureCategory.DeviceDisconnected
                : E2EFailureCategory.ScreenshotInfrastructureFailure;
            return new(false, path, category, result.StandardError.Length == 0 ? "ADB screenshot capture failed." : result.StandardError);
        }
        return new(true, path);
    }

    public Task StartCheckpointMonitorAsync(string serial, string runToken, Func<string, Task> handler, CancellationToken ct = default)
    {
        if (!enableMonitor) return Task.CompletedTask;
        var info = new ProcessStartInfo(PatrolDeviceManager.ResolveAdbCommand(options))
        {
            WorkingDirectory = repository, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var argument in new[] { "-s", serial, "logcat", "-v", "raw", "-T", "1" }) info.ArgumentList.Add(argument);
        monitor = Process.Start(info);
        if (monitor is null) throw new InvalidOperationException("Could not start adb logcat checkpoint monitor.");
        var prefix = $"ONLINEOS_QA_CHECKPOINT|{runToken}|";
        monitorTask = Task.Run(async () =>
        {
            while (await monitor.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                var marker = line.IndexOf(prefix, StringComparison.Ordinal);
                if (marker >= 0) await handler(line[(marker + prefix.Length)..].Trim());
            }
        }, ct);
        return Task.CompletedTask;
    }

    public async Task StopCheckpointMonitorAsync(CancellationToken ct = default)
    {
        if (monitor is null) return;
        if (!monitor.HasExited) monitor.Kill(entireProcessTree: true);
        if (monitorTask is not null)
        {
            try { await monitorTask; } catch (OperationCanceledException) when (ct.IsCancellationRequested || monitor.HasExited) { }
        }
        monitor.Dispose();
        monitor = null;
        monitorTask = null;
    }

    public static string Sanitize(string name)
    {
        var safe = new string(name.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "checkpoint" : safe;
    }
}
