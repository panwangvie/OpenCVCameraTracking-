namespace OpenCVCameraTracking.Core.Camera;

public enum CameraSourceKind
{
    Device,
    Stream,
    File
}

/// <summary>
/// Selects the OpenCV video I/O backend. Auto keeps the platform fallback
/// behavior; explicit values help diagnose camera or codec compatibility.
/// </summary>
public enum VideoCaptureBackend
{
    Auto,
    DirectShow,
    MediaFoundation,
    Ffmpeg
}

public sealed record CameraSourceOptions
{
    public CameraSourceKind Kind { get; init; } = CameraSourceKind.Device;
    public int DeviceIndex { get; init; }
    public string? Address { get; init; }
    /// <summary>Optional alternate address exposed by a camera as its sub stream.</summary>
    public string? SubAddress { get; init; }
    /// <summary>When true and <see cref="SubAddress"/> is present, the sub stream is used.</summary>
    public bool UseSubStream { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? FramesPerSecond { get; init; }
    public VideoCaptureBackend PreferredBackend { get; init; } = VideoCaptureBackend.Auto;
    public int OpenTimeoutMilliseconds { get; init; } = 5_000;
    public int ReadTimeoutMilliseconds { get; init; } = 3_000;
    public bool PreferTcpForRtsp { get; init; } = true;
    public bool LowLatencyMode { get; init; } = true;
    public int ReconnectInitialDelayMilliseconds { get; init; } = 500;
    public int ReconnectMaximumDelayMilliseconds { get; init; } = 10_000;
}
