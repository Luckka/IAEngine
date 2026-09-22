using System.Diagnostics;
using System.Text;
using OnlineOs.AiOrchestrator.Abstractions;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    public Task<ProcessResult> StartDetachedAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
    {
        var start = Stopwatch.StartNew();
        var info = new ProcessStartInfo { WorkingDirectory = spec.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true };
        if (OperatingSystem.IsWindows())
        {
            info.FileName = spec.FileName;
            foreach (var argument in spec.Arguments) info.ArgumentList.Add(argument);
        }
        else if (OperatingSystem.IsMacOS() && File.Exists("/bin/launchctl"))
        {
            // launchd owns the emulator process independently of the short-lived
            // orchestrator, whose host runner may clean up descendant processes.
            info.FileName = "/bin/launchctl";
            info.ArgumentList.Add("submit");
            info.ArgumentList.Add("-l");
            info.ArgumentList.Add("onlineos-ai-orchestrator-emulator");
            info.ArgumentList.Add("--");
            info.ArgumentList.Add(spec.FileName);
            foreach (var argument in spec.Arguments) info.ArgumentList.Add(argument);
        }
        else
        {
            // Background the child from a short-lived shell so it is not retained in
            // the orchestrator's process tree when the QA command exits.
            info.FileName = "/bin/sh";
            info.ArgumentList.Add("-c");
            var command = string.Join(' ', new[] { "nohup", spec.FileName }.Concat(spec.Arguments).Select(ShellQuote));
            info.ArgumentList.Add($"{command} >/dev/null 2>&1 </dev/null &");
        }
        try
        {
            Process.Start(info);
            return Task.FromResult(new ProcessResult(Render(spec), 0, "", "", start.Elapsed));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(new ProcessResult(Render(spec), -1, "", exception.Message, start.Elapsed));
        }
    }

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
    {
        var start = Stopwatch.StartNew();
        var info = new ProcessStartInfo
        {
            FileName = spec.FileName,
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = spec.StandardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in spec.Arguments) info.ArgumentList.Add(argument);

        try
        {
            using var process = new Process { StartInfo = info };
            process.Start();
            var stdout = ReadOutputAsync(process.StandardOutput, spec, cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            if (spec.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(spec.StandardInput.AsMemory(), cancellationToken);
                process.StandardInput.Close();
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(spec.Timeout ?? TimeSpan.FromMinutes(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                return new ProcessResult(Render(spec), -1, await stdout, await stderr, start.Elapsed, true);
            }
            return new ProcessResult(Render(spec), process.ExitCode, await stdout, await stderr, start.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ProcessResult(Render(spec), -1, "", exception.Message, start.Elapsed);
        }
    }

    private static string Render(ProcessSpec spec) => string.Join(' ', new[] { spec.FileName }.Concat(spec.Arguments.Select(Quote)));
    private static string Quote(string value) => value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"")}\"" : value;
    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''")}'";

    private static async Task<string> ReadOutputAsync(StreamReader reader, ProcessSpec spec, CancellationToken ct)
    {
        if (spec.StandardOutputFile is not null)
        {
            await using var file = File.Create(spec.StandardOutputFile);
            await reader.BaseStream.CopyToAsync(file, ct);
            return "";
        }

        var output = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            output.AppendLine(line);
            if (spec.StandardOutputLineHandler is not null) await spec.StandardOutputLineHandler(line);
        }
        return output.ToString();
    }
}
