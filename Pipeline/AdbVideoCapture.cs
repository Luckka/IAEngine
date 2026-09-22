using System.Diagnostics;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;

namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed class AdbVideoCapture(IProcessRunner processes, QaOptions options, string repository)
{
    private Process? recording;
    private string? remotePath;

    public Task StartAsync(string serial, CancellationToken ct = default)
    {
        remotePath = $"/sdcard/onlineos-qa-{Guid.NewGuid():N}.mp4";
        var info = new ProcessStartInfo(PatrolDeviceManager.ResolveAdbCommand(options))
        {
            WorkingDirectory = repository,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-s", serial, "shell", "screenrecord", remotePath }) info.ArgumentList.Add(argument);
        recording = Process.Start(info) ?? throw new InvalidOperationException("Could not start adb screenrecord.");
        return Task.CompletedTask;
    }

    public async Task<string?> StopAsync(string serial, string directory, CancellationToken ct = default)
    {
        if (recording is null || remotePath is null) return null;
        if (!recording.HasExited) recording.Kill(entireProcessTree: true);
        await recording.WaitForExitAsync(ct);
        var path = Path.Combine(directory, "videos", "qa.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pull = await processes.RunAsync(new Models.ProcessSpec(PatrolDeviceManager.ResolveAdbCommand(options), ["-s", serial, "pull", remotePath, path], repository, Timeout: TimeSpan.FromSeconds(30)), ct);
        await processes.RunAsync(new Models.ProcessSpec(PatrolDeviceManager.ResolveAdbCommand(options), ["-s", serial, "shell", "rm", remotePath], repository, Timeout: TimeSpan.FromSeconds(10)), ct);
        return pull.Succeeded && File.Exists(path) ? path : null;
    }
}
