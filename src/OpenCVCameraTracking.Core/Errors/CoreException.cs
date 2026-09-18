namespace OpenCVCameraTracking.Core;

public enum CoreErrorCode
{
    CompositeDetectorRequired,
    RestrictedZoneInvalid,
    EngineAlreadyRunning,
    SourceOpenCancelled,
    SourceOpenFailed,
    SourceAddressRequired,
    RtspReaderFailed,
    HaarModelNotFound,
    HaarModelLoadFailed,
    YoloModelNotFound,
    YoloModelLoadFailed,
    YoloOutputDimensionsUnsupported,
    YoloOutputShapeUnsupported,
    YoloXModelNotFound,
    YoloXModelLoadFailed,
    YoloXOutputShapeUnexpected,
    YuNetModelNotFound,
    YuNetModelLoadFailed,
    SFaceModelNotFound,
    SFaceModelLoadFailed
}

public sealed class CoreException : Exception
{
    public CoreException(CoreErrorCode code, params object[] arguments)
        : base(code.ToString())
    {
        Code = code;
        Arguments = arguments;
    }

    public CoreException(CoreErrorCode code, Exception innerException, params object[] arguments)
        : base(code.ToString(), innerException)
    {
        Code = code;
        Arguments = arguments;
    }

    public CoreErrorCode Code { get; }

    public object[] Arguments { get; }
}
