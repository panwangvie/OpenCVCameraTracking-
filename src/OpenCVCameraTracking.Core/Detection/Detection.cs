using OpenCvSharp;

namespace OpenCVCameraTracking.Core.Detection;

public sealed record Detection(
    Rect Box,
    string Label,
    float Confidence,
    int ClassId = -1,
    IReadOnlyList<Point2f>? Landmarks = null);

public sealed record TrackedObject(
    int Id,
    Rect Box,
    string Label,
    float Confidence,
    string? IdentityName = null,
    bool? IsKnown = null,
    double? RecognitionDistance = null,
    double? RecognitionSimilarity = null,
    IReadOnlyList<Point2f>? Landmarks = null,
    int Misses = 0);
