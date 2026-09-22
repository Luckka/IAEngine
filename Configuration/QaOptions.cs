namespace OnlineOs.AiOrchestrator.Configuration;

public sealed class QaOptions
{
    public string ProjectPath { get; init; } = "";
    public string PatrolCommand { get; init; } = "";
    public string AdbCommand { get; init; } = "";
    public string EmulatorCommand { get; init; } = "";
    public string Device { get; init; } = "";
    public string? EmulatorAvd { get; init; }
    public string? EmulatorFallbackAvd { get; init; }
    public int DeviceReadyTimeoutSeconds { get; init; } = 90;
    public int TestTimeoutSeconds { get; init; } = 900;
    public int MaxInfrastructureRetries { get; init; } = 1;
    public bool CaptureScreenshots { get; init; } = true;
    public bool EnableVideoCapture { get; init; }
    public bool PatrolSupportsScreenshotOutput { get; init; }
    public bool PatrolSupportsVideo { get; init; }
    public string ScreenshotsDirectory { get; init; } = "screenshots";
    public string VideosDirectory { get; init; } = "videos";
}
