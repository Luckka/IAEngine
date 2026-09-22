using System.Net;
using System.Text;
using System.Text.Json;
using OnlineOs.AiOrchestrator.Models;

namespace OnlineOs.AiOrchestrator.Pipeline;

public static class QaReportWriter
{
    public static async Task<string> WriteAsync(string directory, E2ETestResult result, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var html = new StringBuilder("<!doctype html><meta charset='utf-8'><title>OnlineOS Visual QA</title><style>body{font:16px system-ui;margin:2rem;background:#f7f8fb;color:#182033}img{max-width:360px;max-height:640px;margin:8px;border:1px solid #ccd2df;border-radius:8px}section{background:white;padding:1rem;margin:1rem 0;border-radius:10px}a{color:#3047a8}</style>");
        html.Append($"<h1>OnlineOS — Visual QA Report</h1><p><b>Run:</b> {WebUtility.HtmlEncode(result.Request.RunId)}<br><b>Profile:</b> {result.Request.Profile}<br><b>Device:</b> {WebUtility.HtmlEncode(result.Request.Device)}<br><b>Patrol executable:</b> {WebUtility.HtmlEncode(result.PatrolExecutable ?? "unresolved")}<br><b>Patrol working directory:</b> {WebUtility.HtmlEncode(result.PatrolWorkingDirectory ?? "unresolved")}<br><b>Status:</b> {(result.Success ? "PASS" : "FAIL")}</p>");
        html.Append("<section><h2>Executed E2E</h2><ul>");
        foreach (var test in result.SelectedTests) html.Append($"<li>{WebUtility.HtmlEncode(test)}</li>");
        html.Append("</ul></section><section><h2>Screenshots</h2>");
        foreach (var image in result.Screenshots) { var relative = Path.GetRelativePath(directory, image).Replace(Path.DirectorySeparatorChar, '/'); html.Append($"<a href='{WebUtility.HtmlEncode(relative)}'><img src='{WebUtility.HtmlEncode(relative)}' alt='{WebUtility.HtmlEncode(Path.GetFileName(image))}'></a>"); }
        foreach (var missing in result.MissingScreenshots ?? []) html.Append($"<p>Missing screenshot: {WebUtility.HtmlEncode(missing)}</p>");
        html.Append("</section><section><h2>Videos</h2><ul>");
        foreach (var video in result.Videos) { var relative = Path.GetRelativePath(directory, video).Replace(Path.DirectorySeparatorChar, '/'); html.Append($"<li><a href='{WebUtility.HtmlEncode(relative)}'>{WebUtility.HtmlEncode(Path.GetFileName(video))}</a></li>"); }
        html.Append("</ul></section>");
        if (!result.Success) html.Append($"<section><h2>Failure</h2><p>{WebUtility.HtmlEncode(result.FailureCategory + ": " + result.FailureDetail)}</p></section>");
        var path = Path.Combine(directory, "report.html");
        await File.WriteAllTextAsync(path, html.ToString(), ct);
        return path;
    }
}
