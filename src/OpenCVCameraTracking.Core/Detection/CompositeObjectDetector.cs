using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace OpenCVCameraTracking.Core.Detection;

/// <summary>
/// Composes multiple object detectors into one. On every frame each child
/// detector runs in sequence, and the results are merged. Overlapping boxes
/// that share the same label are de-duplicated with NMS so composite models
/// never report duplicate targets.
/// </summary>
public sealed class CompositeObjectDetector : IObjectDetector
{
    private readonly IReadOnlyList<IObjectDetector> _detectors;
    private readonly float _nmsThreshold;

    public CompositeObjectDetector(
        IEnumerable<IObjectDetector> detectors,
        float nmsThreshold = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(detectors);
        _detectors = detectors.ToArray();
        if (_detectors.Count == 0)
        {
            throw new ArgumentException("At least one detector is required.", nameof(detectors));
        }

        _nmsThreshold = nmsThreshold;
    }

    public IReadOnlyList<Detection> Detect(Mat bgrFrame)
    {
        if (_detectors.Count == 1)
        {
            return _detectors[0].Detect(bgrFrame);
        }

        // OpenCV DNN models and the Mat passed by the capture loop are native
        // resources. Running multiple detectors against the same Mat on worker
        // tasks made this synchronous API block waiting for child tasks and
        // risked native-resource contention. The capture loop is already off
        // the UI thread, so deterministic sequential execution is safer.
        var merged = new List<Detection>();
        foreach (var detector in _detectors)
        {
            merged.AddRange(detector.Detect(bgrFrame));
        }

        return RemoveDuplicates(merged);
    }

    private IReadOnlyList<Detection> RemoveDuplicates(IReadOnlyList<Detection> detections)
    {
        if (detections.Count == 0)
        {
            return detections;
        }

        var kept = new List<Detection>();
        foreach (var group in detections.GroupBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
        {
            var boxes = group.Select(item => item.Box).ToArray();
            var scores = group.Select(item => item.Confidence).ToArray();
            CvDnn.NMSBoxes(boxes, scores, 0f, _nmsThreshold, out var indices);
            foreach (var index in indices)
            {
                kept.Add(group.ElementAt(index));
            }
        }

        return kept;
    }

    public void Dispose()
    {
        foreach (var detector in _detectors)
        {
            detector.Dispose();
        }
    }
}
