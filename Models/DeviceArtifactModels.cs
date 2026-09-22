namespace OnlineOs.AiOrchestrator.Models;

public sealed record ScreenshotCaptureResult(bool Captured, string Path, E2EFailureCategory FailureCategory = E2EFailureCategory.None, string Detail = "");
