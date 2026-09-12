using OpenCVCameraTracking.Core.Camera;

namespace OpenCVCameraTracking;

/// <summary>One selectable source used by the multi-camera preview window.</summary>
public sealed record MultiPreviewSource(
    string Key,
    string Name,
    string Description,
    CameraSourceOptions Options)
{
    public string DisplayName => $"{Name} · {Description}";
}
