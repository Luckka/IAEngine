using System.Text.Json;
using System.Text.RegularExpressions;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Models;
using OnlineOs.AiOrchestrator.Pipeline;

namespace OnlineOs.AiOrchestrator.Infrastructure;

public sealed partial class RunStore(string repositoryRoot, string runsDirectory) : IRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    private string Root => new WorkspaceBoundary(repositoryRoot).Resolve(runsDirectory);
    private string ActivePointer => Path.Combine(Root, "active-run.json");

    public async Task InitializeAsync(RunRecord run, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.Combine(Root, run.RunId));
        await SaveArtifactAsync(run.RunId, "task.json", run.Task, ct);
        await SaveAsync(run, ct);
    }

    public async Task SaveAsync(RunRecord run, CancellationToken ct = default)
    {
        run.NormalizeForState();
        await SaveArtifactAsync(run.RunId, "run.json", run, ct);
        Directory.CreateDirectory(Root);
        if (run.State is WorkflowState.Approved or WorkflowState.HumanRequired or WorkflowState.Failed or WorkflowState.Completed or WorkflowState.Abandoned)
        {
            await ClearPointerIfOwnedAsync(run.RunId, ct);
            return;
        }
        var pointer = JsonSerializer.Serialize(new { runId = run.RunId, updatedAt = DateTimeOffset.UtcNow }, JsonOptions);
        var temporary = ActivePointer + ".tmp";
        await File.WriteAllTextAsync(temporary, pointer, ct);
        File.Move(temporary, ActivePointer, true);
    }

    public async Task SaveArtifactAsync<T>(string runId, string name, T value, CancellationToken ct = default)
    {
        var safeName = Path.GetFileName(name);
        var path = Path.Combine(Root, runId, safeName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(value, JsonOptions);
        json = SecretPattern().Replace(json, "$1: \"[REDACTED]\"");
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, json, ct);
        File.Move(temporary, path, true);
    }

    public async Task<RunRecord?> LoadAsync(string runId, CancellationToken ct = default)
    {
        var path = Path.Combine(Root, Path.GetFileName(runId), "run.json");
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<RunRecord>(await File.ReadAllTextAsync(path, ct), JsonOptions);
    }

    public async Task<T?> LoadLatestArtifactAsync<T>(string runId, string prefix, CancellationToken ct = default)
    {
        var directory = Path.Combine(Root, Path.GetFileName(runId));
        var path = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, $"{prefix}*.json").OrderByDescending(x => x, StringComparer.Ordinal).FirstOrDefault()
            : null;
        return path is null ? default : JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), JsonOptions);
    }

    public async Task<T?> LoadArtifactAsync<T>(string runId, string name, CancellationToken ct = default)
    {
        var path = Path.Combine(Root, Path.GetFileName(runId), Path.GetFileName(name));
        return File.Exists(path) ? JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), JsonOptions) : default;
    }

    public async Task<RunRecord?> FindActiveAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(Root)) return null;
        if (File.Exists(ActivePointer))
        {
            using var pointer = JsonDocument.Parse(await File.ReadAllTextAsync(ActivePointer, ct));
            var runId = pointer.RootElement.GetProperty("runId").GetString();
            if (!string.IsNullOrWhiteSpace(runId)) return await LoadAsync(runId, ct);
        }
        foreach (var path in Directory.EnumerateFiles(Root, "run.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var run = JsonSerializer.Deserialize<RunRecord>(await File.ReadAllTextAsync(path, ct), JsonOptions);
            if (run is not null && !RunHealthEvaluator.Assess(run).IsTerminal) return run;
        }
        return null;
    }

    public async Task<RunHealthAssessment> AssessActiveAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(Root)) return new(RunHealth.Terminal, "No active run pointer exists.");
        if (File.Exists(ActivePointer))
        {
            try
            {
                using var pointer = JsonDocument.Parse(await File.ReadAllTextAsync(ActivePointer, ct));
                if (!pointer.RootElement.TryGetProperty("runId", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
                {
                    await RepairPointerAsync(ct);
                    return new(RunHealth.MalformedPointer, "active-run.json has no valid runId; pointer was repaired.");
                }
                var runId = id.GetString()!;
                var run = await LoadAsync(runId, ct);
                if (run is null)
                {
                    await RepairPointerAsync(ct);
                    return new(RunHealth.MissingRun, $"active-run.json references missing run {runId}; pointer was repaired.");
                }
                var health = RunHealthEvaluator.Assess(run);
                if (health.IsTerminal) await RepairPointerAsync(ct);
                return health with { Reason = $"{runId}: {health.Reason}" };
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                await RepairPointerAsync(ct);
                return new(RunHealth.MalformedPointer, "active-run.json is malformed; pointer was repaired.");
            }
        }
        var runWithoutPointer = await FindActiveAsync(ct);
        return runWithoutPointer is null ? new(RunHealth.Terminal, "No active run exists.") : RunHealthEvaluator.Assess(runWithoutPointer);
    }

    public async Task AbandonAsync(string runId, string reason, CancellationToken ct = default)
    {
        var run = await LoadAsync(runId, ct) ?? throw new InvalidOperationException($"Run '{runId}' does not exist.");
        var health = RunHealthEvaluator.Assess(run);
        if (health.CanResume) throw new InvalidOperationException($"Run {runId} is resumable ({health.NextAction}). Use continue; it was not abandoned.");
        if (health.Health == RunHealth.HumanRequired) throw new InvalidOperationException($"Run {runId} requires human action; it was not abandoned.");
        if (health.IsTerminal) { await RepairPointerAsync(ct); return; }
        run.AbandonmentReason = reason;
        run.AbandonedAt = DateTimeOffset.UtcNow;
        run.EndedAt = run.AbandonedAt;
        run.State = WorkflowState.Abandoned;
        run.Transitions.Add(new StateTransition(run.CurrentStage ?? WorkflowState.Created, WorkflowState.Abandoned, run.AbandonedAt.Value, reason));
        await SaveAsync(run, ct);
    }

    private async Task RepairPointerAsync(CancellationToken ct)
    {
        if (!File.Exists(ActivePointer)) return;
        ct.ThrowIfCancellationRequested();
        var retired = ActivePointer + $".repaired-{Guid.NewGuid():N}";
        File.Move(ActivePointer, retired);
        File.Delete(retired);
    }

    private async Task ClearPointerIfOwnedAsync(string runId, CancellationToken ct)
    {
        if (!File.Exists(ActivePointer)) return;
        try
        {
            using var pointer = JsonDocument.Parse(await File.ReadAllTextAsync(ActivePointer, ct));
            if (!pointer.RootElement.TryGetProperty("runId", out var id) || id.GetString() != runId) return;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return; }
        await RepairPointerAsync(ct);
    }

    [GeneratedRegex("(?i)(\\\"(?:token|password|secret|apiKey)\\\")\\s*:\\s*\\\"[^\\\"]*\\\"")]
    private static partial Regex SecretPattern();
}
