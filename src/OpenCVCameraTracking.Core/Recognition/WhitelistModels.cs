namespace OpenCVCameraTracking.Core.Recognition;

public enum WhitelistSubjectKind
{
    Face,
    Cat
}

public enum WhitelistEnrollmentStatus
{
    Success,
    InvalidName,
    NoFrame,
    NoMatchingTarget,
    RecognitionUnavailable
}

public sealed record WhitelistProfile(
    string Id,
    int NumericId,
    string Name,
    WhitelistSubjectKind Kind,
    DateTimeOffset CreatedAt,
    int SampleCount);

public sealed record WhitelistEnrollmentResult(
    WhitelistEnrollmentStatus Status,
    WhitelistProfile? Profile = null);
