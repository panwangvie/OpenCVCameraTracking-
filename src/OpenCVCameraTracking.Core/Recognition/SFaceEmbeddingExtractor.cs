using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace OpenCVCameraTracking.Core.Recognition;

/// <summary>
/// OpenCV SFace inference implemented with the DNN API. OpenCvSharp exposes
/// FaceDetectorYN but does not currently expose FaceRecognizerSF directly.
/// </summary>
internal sealed class SFaceEmbeddingExtractor : IDisposable
{
    private const int InputSize = 112;
    private static readonly Point2f[] TargetLandmarks =
    [
        new(38.2946f, 51.6963f),
        new(73.5318f, 51.5014f),
        new(56.0252f, 71.7366f),
        new(41.5493f, 92.3655f),
        new(70.7299f, 92.2041f)
    ];

    private readonly Net _network;

    public SFaceEmbeddingExtractor(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("SFace recognition model was not found.", modelPath);
        }

        _network = CvDnn.ReadNetFromOnnx(modelPath)
            ?? throw new InvalidOperationException($"Unable to load SFace recognition model: {modelPath}");
        _network.SetPreferableBackend(Backend.OPENCV);
        _network.SetPreferableTarget(Target.CPU);
    }

    public Mat AlignFace(
        Mat bgrFrame,
        Rect faceBox,
        IReadOnlyList<Point2f>? landmarks)
    {
        if (landmarks is { Count: 5 } && TryCreateSimilarityTransform(landmarks, out var transform))
        {
            using (transform)
            {
                var aligned = new Mat();
                Cv2.WarpAffine(
                    bgrFrame,
                    aligned,
                    transform,
                    new Size(InputSize, InputSize),
                    InterpolationFlags.Linear,
                    BorderTypes.Constant,
                    Scalar.Black);
                return EnsureBgr(aligned);
            }
        }

        var bounds = new Rect(0, 0, bgrFrame.Width, bgrFrame.Height);
        var box = faceBox & bounds;
        if (box.Width < 24 || box.Height < 24)
        {
            return new Mat();
        }

        using var roi = new Mat(bgrFrame, box);
        var resized = new Mat();
        Cv2.Resize(roi, resized, new Size(InputSize, InputSize), interpolation: InterpolationFlags.Area);
        return EnsureBgr(resized);
    }

    public float[] Extract(Mat alignedFace)
    {
        if (alignedFace.Empty())
        {
            return [];
        }

        using var bgr = EnsureBgr(alignedFace.Clone());
        using var blob = CvDnn.BlobFromImage(
            bgr,
            scaleFactor: 1d,
            size: new Size(InputSize, InputSize),
            mean: Scalar.Black,
            swapRB: true,
            crop: false);
        _network.SetInput(blob);
        using var output = _network.Forward();
        output.GetArray(out float[] values);

        var normSquared = 0d;
        foreach (var value in values)
        {
            normSquared += value * value;
        }

        var norm = Math.Sqrt(normSquared);
        if (norm <= double.Epsilon)
        {
            return [];
        }

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = (float)(values[index] / norm);
        }

        return values;
    }

    public static double CosineSimilarity(IReadOnlyList<float> first, IReadOnlyList<float> second)
    {
        if (first.Count == 0 || first.Count != second.Count)
        {
            return -1d;
        }

        var dot = 0d;
        for (var index = 0; index < first.Count; index++)
        {
            dot += first[index] * second[index];
        }

        return Math.Clamp(dot, -1d, 1d);
    }

    private static bool TryCreateSimilarityTransform(
        IReadOnlyList<Point2f> source,
        out Mat transform)
    {
        transform = new Mat();
        var sourceMeanX = source.Average(point => (double)point.X);
        var sourceMeanY = source.Average(point => (double)point.Y);
        var targetMeanX = TargetLandmarks.Average(point => (double)point.X);
        var targetMeanY = TargetLandmarks.Average(point => (double)point.Y);

        var denominator = 0d;
        var scaleRotationA = 0d;
        var scaleRotationB = 0d;
        for (var index = 0; index < source.Count; index++)
        {
            var sourceX = source[index].X - sourceMeanX;
            var sourceY = source[index].Y - sourceMeanY;
            var targetX = TargetLandmarks[index].X - targetMeanX;
            var targetY = TargetLandmarks[index].Y - targetMeanY;
            denominator += sourceX * sourceX + sourceY * sourceY;
            scaleRotationA += targetX * sourceX + targetY * sourceY;
            scaleRotationB += targetY * sourceX - targetX * sourceY;
        }

        if (denominator <= double.Epsilon)
        {
            return false;
        }

        var a = scaleRotationA / denominator;
        var b = scaleRotationB / denominator;
        var translateX = targetMeanX - a * sourceMeanX + b * sourceMeanY;
        var translateY = targetMeanY - b * sourceMeanX - a * sourceMeanY;

        transform = new Mat(2, 3, MatType.CV_64FC1);
        transform.Set(0, 0, a);
        transform.Set(0, 1, -b);
        transform.Set(0, 2, translateX);
        transform.Set(1, 0, b);
        transform.Set(1, 1, a);
        transform.Set(1, 2, translateY);
        return true;
    }

    private static Mat EnsureBgr(Mat image)
    {
        if (image.Empty() || image.Channels() == 3)
        {
            return image;
        }

        var converted = new Mat();
        Cv2.CvtColor(
            image,
            converted,
            image.Channels() == 4
                ? ColorConversionCodes.BGRA2BGR
                : ColorConversionCodes.GRAY2BGR);
        image.Dispose();
        return converted;
    }

    public void Dispose() => _network.Dispose();
}
