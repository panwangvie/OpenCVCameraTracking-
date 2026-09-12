using System.Text.Json;
using OpenCVCameraTracking.Core.Camera;

namespace OpenCVCameraTracking.Configuration;

public sealed class ApplicationSettings
{
    public string Language { get; set; } = "zh-CN";
    public string SelectedSourceKind { get; set; } = "Device";
    public string SelectedDetectionMode { get; set; } = "Face";
    public string AnimalModelChoice { get; set; } = "BuiltIn";
    public string CustomAnimalModelPath { get; set; } = string.Empty;
    public string LastStreamAddress { get; set; } = string.Empty;
    public string? SelectedStreamId { get; set; }
    public string SelectedStreamVariant { get; set; } = "Main";
    public bool RtspLowLatency { get; set; } = true;
    public VideoCaptureBackend PreferredBackend { get; set; } = VideoCaptureBackend.Auto;
    public string SelectedLayout { get; set; } = "Single";
    public List<string> LayoutStreamIds { get; set; } = [];
    /// <summary>Selected source keys for the multi-camera picker.</summary>
    public List<string> MultiPreviewSourceKeys { get; set; } = [];
    public float FaceConfidence { get; set; } = 0.55f;
    public float AnimalConfidence { get; set; } = 0.35f;
    /// <summary>
    /// The Store version dismissed by the user. A newer version is shown again.
    /// </summary>
    public string DismissedStoreUpdateVersion { get; set; } = string.Empty;
    public List<StreamProfile> Streams { get; set; } = [];
    public List<CameraDeviceProfile> CameraDevices { get; set; } = [];

    public ApplicationSettings DeepClone() =>
        JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(this)) ?? new ApplicationSettings();
}

public sealed class StreamProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string SubAddress { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Group { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public string AddressFor(bool useSubStream) =>
        useSubStream && !string.IsNullOrWhiteSpace(SubAddress) ? SubAddress : Address;

    public override string ToString() => Name;
}

public sealed class CameraDeviceProfile
{
    public int DeviceIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string DisplayName => $"{DeviceIndex}: {Name}";
}
