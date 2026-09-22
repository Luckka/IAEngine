using System.Text.Json;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed class PatrolE2ETestRunner(
    IProcessRunner processes,
    IE2EDeviceManager devices,
    QaOptions options,
    string repository,
    IDeviceArtifactCapture? artifactCapture = null,
    AdbVideoCapture? videoCapture = null,
    IPatrolExecutableResolver? patrolResolver = null,
    Action<string>? diagnostics = null) : IE2ETestRunner
{
    public async Task<E2ETestResult> RunAsync(E2ETestRequest request, CancellationToken ct = default)
    {
        var directory = request.ArtifactDirectory ?? Path.Combine(repository, ".ai-runs", request.RunId, "qa");
        var screenshots = Path.Combine(directory, options.ScreenshotsDirectory);
        var videos = Path.Combine(directory, options.VideosDirectory);
        var workingDirectory = Path.GetFullPath(Path.Combine(repository, options.ProjectPath));
        if (!Directory.Exists(workingDirectory))
            return await PersistAsync(request, [], directory, [], [], -1, TimeSpan.Zero, E2EFailureCategory.QaConfigurationFailure,
                $"Patrol working directory does not exist: {workingDirectory}", "", [], null, workingDirectory, ct);
        var resolution = (patrolResolver ?? new PatrolExecutableResolver(options, repository)).Resolve();
        if (!resolution.Available || resolution.ExecutablePath is null)
            return await PersistAsync(request, [], directory, [], [], -1, TimeSpan.Zero, E2EFailureCategory.PatrolCliUnavailable,
                resolution.Detail, "", [], null, workingDirectory, ct);
        Directory.CreateDirectory(screenshots);
        Directory.CreateDirectory(videos);
        IReadOnlyList<string> selected;
        try
        {
            selected = NormalizeTargets(request.Target is null
                ? PatrolTestSelector.Select(request.Profile, milestoneId: request.MilestoneId)
                : [request.Target], repository, options.ProjectPath);
        }
        catch (ArgumentException exception)
        {
            return await PersistAsync(request, [], directory, [], [], -1, TimeSpan.Zero, E2EFailureCategory.QaConfigurationFailure, exception.Message, "", [], resolution.ExecutablePath, workingDirectory, ct);
        }
        var device = await devices.EnsureReadyAsync(request, ct);
        if (!device.Ready)
            return await PersistAsync(request, selected, directory, [], [], -1, TimeSpan.Zero, device.FailureCategory, device.Detail, "", [], resolution.ExecutablePath, workingDirectory, ct);

        var selectedRequest = request with { Device = device.Device };
        diagnostics?.Invoke($"Patrol executable: {resolution.ExecutablePath}");
        diagnostics?.Invoke($"Patrol working directory: {workingDirectory}");
        diagnostics?.Invoke($"Device: {device.Device}");
        var arguments = new List<string> { "test", "--device", device.Device };
        arguments.AddRange(["--dart-define", $"ONLINEOS_QA_RUN_ID={request.RunId}"]);
        if (selected.Count == 1) arguments.AddRange(["--target", selected[0]]);
        if (request.CaptureScreenshots && options.PatrolSupportsScreenshotOutput)
            arguments.AddRange(["--screenshots-output-dir", screenshots]);
        if (request.RecordVideo && options.PatrolSupportsVideo) arguments.Add("--record-video");
        var captured = new List<string>();
        var missing = new List<string>();
        var checkpointNames = new HashSet<string>(StringComparer.Ordinal);
        var capture = artifactCapture ?? new AdbDeviceArtifactCapture(processes, options, repository);
        if (request.RecordVideo && options.PatrolSupportsVideo && videoCapture is not null)
            await videoCapture.StartAsync(device.Device, ct);
        async Task CaptureCheckpointAsync(string rawName)
        {
            if (!request.CaptureScreenshots) return;
            var name = rawName.Trim();
            if (name.Length == 0) return;
            var safeName = AdbDeviceArtifactCapture.Sanitize(name);
            if (!checkpointNames.Add(safeName)) return;
            var result = await capture.CaptureScreenshotAsync(device.Device, name, screenshots, ct);
            if (result.Captured) captured.Add(result.Path);
            else missing.Add($"{safeName}: {result.FailureCategory} — {result.Detail}");
        }
        async Task HandleOutputAsync(string line)
        {
            const string prefix = "ONLINEOS_QA_CHECKPOINT|";
            if (line.StartsWith(prefix, StringComparison.Ordinal)) await CaptureCheckpointAsync(line[prefix.Length..].Trim());
        }
        if (capture is AdbDeviceArtifactCapture adbCapture)
            await adbCapture.StartCheckpointMonitorAsync(device.Device, request.RunId, CaptureCheckpointAsync, ct);
        var process = await processes.RunAsync(new ProcessSpec(resolution.ExecutablePath, arguments, workingDirectory,
            Timeout: TimeSpan.FromSeconds(options.TestTimeoutSeconds), StandardOutputLineHandler: HandleOutputAsync), ct);
        if (capture is AdbDeviceArtifactCapture monitoredCapture) await monitoredCapture.StopCheckpointMonitorAsync(ct);
        var recordedVideo = request.RecordVideo && options.PatrolSupportsVideo && videoCapture is not null
            ? await videoCapture.StopAsync(device.Device, directory, ct)
            : null;
        var failure = process.Succeeded ? E2EFailureCategory.None : Classify(process);
        var images = captured.Count > 0 ? captured.ToArray() : Directory.Exists(screenshots) ? Directory.GetFiles(screenshots, "*.png", SearchOption.AllDirectories) : [];
        var videoFiles = recordedVideo is not null ? [recordedVideo] : Directory.Exists(videos) ? Directory.GetFiles(videos, "*.mp4", SearchOption.AllDirectories) : [];
        return await PersistAsync(selectedRequest, selected, directory, images, videoFiles, process.ExitCode, process.Duration, failure, process.StandardError, process.StandardOutput, missing, resolution.ExecutablePath, workingDirectory, ct);
    }

    public static IReadOnlyList<string> NormalizeTargets(IReadOnlyList<string> targets, string repository, string projectPath)
    {
        var appRoot = Path.GetFullPath(Path.Combine(repository, projectPath));
        var normalized = new List<string>(targets.Count);
        foreach (var target in targets)
        {
            var absolute = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(appRoot, target));
            var relative = Path.GetRelativePath(appRoot, absolute);
            if (relative is "." or ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new ArgumentException($"Patrol target '{target}' is outside the Flutter app root '{appRoot}'.");
            normalized.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
        }
        return normalized;
    }

    private static E2EFailureCategory Classify(ProcessResult process)
    {
        var text = process.StandardError + " " + process.StandardOutput;
        if (process.TimedOut) return E2EFailureCategory.Timeout;
        if (process.ExitCode == -1) return E2EFailureCategory.PatrolCliUnavailable;
        if (text.Contains("adb", StringComparison.OrdinalIgnoreCase) && text.Contains("not found", StringComparison.OrdinalIgnoreCase)) return E2EFailureCategory.AdbFailure;
        if (text.Contains("device offline", StringComparison.OrdinalIgnoreCase) || text.Contains("device disconnected", StringComparison.OrdinalIgnoreCase)) return E2EFailureCategory.DeviceDisconnected;
        if (text.Contains("build failed", StringComparison.OrdinalIgnoreCase) || text.Contains("compilation", StringComparison.OrdinalIgnoreCase)) return E2EFailureCategory.BuildFailure;
        if (text.Contains("not found", StringComparison.OrdinalIgnoreCase) || text.Contains("command not found", StringComparison.OrdinalIgnoreCase)) return E2EFailureCategory.ConfigurationFailure;
        if (text.Contains("expected", StringComparison.OrdinalIgnoreCase) || text.Contains("assert", StringComparison.OrdinalIgnoreCase) || text.Contains("finder", StringComparison.OrdinalIgnoreCase)) return E2EFailureCategory.ProductAssertionFailure;
        return E2EFailureCategory.TestFailure;
    }

    private static async Task<E2ETestResult> PersistAsync(E2ETestRequest request, IReadOnlyList<string> selected, string directory,
        IReadOnlyList<string> screenshots, IReadOnlyList<string> videos, int exitCode, TimeSpan duration,
        E2EFailureCategory failure, string detail, string stdout, IReadOnlyList<string>? missingScreenshots,
        string? patrolExecutable, string? patrolWorkingDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var stdoutPath = Path.Combine(directory, "logs", "stdout.log");
        var stderrPath = Path.Combine(directory, "logs", "stderr.log");
        Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath)!);
        await File.WriteAllTextAsync(stdoutPath, stdout, ct);
        await File.WriteAllTextAsync(stderrPath, detail, ct);
        var resultPath = Path.Combine(directory, "result.json");
        var provisional = new E2ETestResult(failure == E2EFailureCategory.None, exitCode, duration, request, selected.ToArray(), screenshots.ToArray(), videos.ToArray(), stdoutPath, stderrPath, resultPath, "", failure, detail, missingScreenshots?.ToArray(), patrolExecutable, patrolWorkingDirectory);
        var report = await QaReportWriter.WriteAsync(directory, provisional, ct);
        var result = provisional with { ReportPath = report };
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
        return result;
    }
}
