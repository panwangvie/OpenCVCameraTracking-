namespace OpenCVCameraTracking.Core.Camera;

public sealed record CameraDeviceInfo(int Index, string Name, string Group = "", string Notes = "", bool IsEnabled = true)
{
    public override string ToString() => $"{Index}: {Name}";
}
