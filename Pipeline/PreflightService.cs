using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Configuration;
using OnlineOs.AiOrchestrator.Models;
using System.Diagnostics;

namespace OnlineOs.AiOrchestrator.Pipeline;

public sealed class PreflightService(IProcessRunner processes, ITaskRouter router, IGitService git, AppOptions options, string repository)
{
    public async Task<IReadOnlyList<PreflightCheck>> RunAsync(CancellationToken ct = default)
    {
        var checks = new List<PreflightCheck>();
        checks.Add(await CommandCheck("Git", "git", ["--version"], true, ct));
        try { checks.Add(new PreflightCheck("Repository", "OK", true, await git.GetRootAsync(ct))); }
        catch (Exception e) { checks.Add(new PreflightCheck("Repository", "FAIL", true, e.Message)); }
        try
        {
            var branch = await git.GetBranchAsync(ct);
            var safety = await git.CheckBranchSafetyAsync(ct);
            checks.Add(new PreflightCheck("Branch", safety.Safe ? "OK" : "FAIL", true, $"{branch}: {safety.Reason}"));
        }
        catch (Exception e) { checks.Add(new PreflightCheck("Branch", "FAIL", true, e.Message)); }

        var ollama = await router.CheckHealthAsync(ct);
        if (!ollama.Reachable && options.Ollama.AutoStart && OperatingSystem.IsMacOS())
        {
            try
            {
                var executable = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                    .Select(path => Path.Combine(path, "ollama")).FirstOrDefault(File.Exists);
                if (executable is not null)
                {
                    Process.Start(new ProcessStartInfo(executable, "serve") { UseShellExecute = false, CreateNoWindow = true });
                    for (var attempt = 0; attempt < 5 && !ollama.Reachable; attempt++)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
                        ollama = await router.CheckHealthAsync(ct);
                    }
                }
            }
            catch (Exception exception) { ollama = (false, false, $"Automatic Ollama startup failed: {exception.Message}"); }
        }
        if (!ollama.Reachable && options.Orchestrator.PreflightDependencyRetries > 0)
        {
            // A second health probe handles an Ollama process that is still starting
            // without spawning an unmanaged daemon or downloading anything.
            for (var attempt = 0; attempt < options.Orchestrator.PreflightDependencyRetries && !ollama.Reachable; attempt++)
                ollama = await router.CheckHealthAsync(ct);
        }
        if (ollama.Reachable && !ollama.ModelAvailable && options.Ollama.AutoPullConfiguredModel && !string.IsNullOrWhiteSpace(options.Ollama.Model))
        {
            var pull = await processes.RunAsync(new ProcessSpec("ollama", ["pull", options.Ollama.Model], repository, Timeout: TimeSpan.FromMinutes(10)), ct);
            ollama = pull.Succeeded ? await router.CheckHealthAsync(ct) : (true, false, $"Configured Ollama model pull failed: {pull.StandardError.Trim()}");
        }
        checks.Add(new PreflightCheck("Ollama", ollama.Reachable ? "OK" : "FAIL", true, ollama.Detail));
        checks.Add(new PreflightCheck("Ollama Model", ollama.ModelAvailable ? "OK" : "FAIL", true, ollama.Detail));
        checks.Add(await CommandCheck("Claude", options.Claude.Command, ["--version"], true, ct));
        checks.Add(await CommandCheck("Codex", options.Codex.Command, ["--version"], true, ct));

        var needsFlutter = options.Validation.Commands.Any(x => x.Enabled && Path.GetFileName(x.Command).Equals("flutter", StringComparison.OrdinalIgnoreCase));
        checks.Add(needsFlutter
            ? await CommandCheck("Flutter", "flutter", ["--version"], true, ct)
            : new PreflightCheck("Flutter", "NOT REQUIRED", false));
        return checks;
    }

    public static bool CanRun(IReadOnlyList<PreflightCheck> checks) => checks.All(x => !x.Critical || x.Status == "OK");

    private async Task<PreflightCheck> CommandCheck(string name, string command, IReadOnlyList<string> arguments, bool critical, CancellationToken ct)
    {
        var result = await processes.RunAsync(new ProcessSpec(command, arguments, repository, Timeout: TimeSpan.FromSeconds(20)), ct);
        return result.Succeeded
            ? new PreflightCheck(name, "OK", critical, result.StandardOutput.Trim().Split('\n').FirstOrDefault())
            : new PreflightCheck(name, "FAIL", critical, result.StandardError.Trim());
    }
}
