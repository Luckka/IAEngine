using System.Text.Json.Serialization;

namespace OnlineOs.AiOrchestrator.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum E2EProfile { Fast, Milestone, Full }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum E2EFailureCategory { None, ProductAssertionFailure, TestFailure, BuildFailure, EmulatorUnavailable, EmulatorStartupFailure, EmulatorBootTimeout, DeviceDisconnected, AdbFailure, ScreenshotInfrastructureFailure, ArtifactCaptureFailure, PatrolInfrastructureFailure, PatrolCliUnavailable, Timeout, ConfigurationFailure, QaConfigurationFailure }

public sealed record E2ETestRequest(
    string RunId,
    string? TaskId,
    string? MilestoneId,
    E2EProfile Profile,
    string Platform = "android",
    string Device = "",
    string? Target = null,
    bool CaptureScreenshots = true,
    bool RecordVideo = false,
    string? ArtifactDirectory = null,
    int MaxRetries = 1);

public sealed record E2EDeviceResult(bool Ready, bool Reused, string Device, E2EFailureCategory FailureCategory = E2EFailureCategory.None, string Detail = "");

public sealed record E2ETestResult(
    bool Success,
    int ExitCode,
    TimeSpan Duration,
    E2ETestRequest Request,
    string[] SelectedTests,
    string[] Screenshots,
    string[] Videos,
    string StdoutPath,
    string StderrPath,
    string ResultPath,
    string ReportPath,
    E2EFailureCategory FailureCategory = E2EFailureCategory.None,
    string FailureDetail = "",
    string[]? MissingScreenshots = null,
    string? PatrolExecutable = null,
    string? PatrolWorkingDirectory = null);
