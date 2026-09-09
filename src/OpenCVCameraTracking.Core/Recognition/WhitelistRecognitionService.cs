using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCVCameraTracking.Core.Detection;
using OpenCvSharp;
using OpenCvSharp.Face;

namespace OpenCVCameraTracking.Core.Recognition;

/// <summary>
/// Local, lightweight whitelist recognition backed by OpenCV LBPH.
/// Human face boxes are used directly. For cats, the upper portion of the
/// detected cat box is used as a best-effort face/appearance region.
/// </summary>
public sealed class WhitelistRecognitionService : IDisposable
{
    private const int NormalizedSize = 128;
    private readonly object _gate = new();
    private readonly string _storageDirectory;
    private readonly string _samplesDirectory;
    private readonly string _profilesPath;
    private readonly double _faceThreshold;
    private readonly double _catThreshold;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private List<StoredProfile> _profiles;
    private LBPHFaceRecognizer? _faceRecognizer;
    private LBPHFaceRecognizer? _catRecognizer;
    private bool _disposed;

    public WhitelistRecognitionService(
        string? storageDirectory = null,
        double faceThreshold = 62d,
        double catThreshold = 52d)
    {
        _storageDirectory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenCVCameraTracking",
            "Whitelist");
        _samplesDirectory = Path.Combine(_storageDirectory, "samples");
        _profilesPath = Path.Combine(_storageDirectory, "profiles.json");
        _faceThreshold = faceThreshold;
        _catThreshold = catThreshold;

        Directory.CreateDirectory(_samplesDirectory);
        _profiles = LoadProfiles();
        RebuildModels();
    }

    public string StorageDirectory => _storageDirectory;

    public IReadOnlyList<WhitelistProfile> GetProfiles()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return _profiles
                .Select(ToPublicProfile)
                .OrderByDescending(profile => profile.CreatedAt)
                .ThenBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<string> GetSamplePaths(string profileId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var profile = _profiles.FirstOrDefault(item => item.Id == profileId);
            if (profile is null)
            {
                return [];
            }

            var directory = GetProfileDirectory(profile.Id);
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.png")
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }
    }

    public IReadOnlyList<TrackedObject> Recognize(
        Mat bgrFrame,
        IReadOnlyList<TrackedObject> objects)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var results = new TrackedObject[objects.Count];
            for (var index = 0; index < objects.Count; index++)
            {
                var item = objects[index];
                if (!TryGetKind(item.Label, out var kind))
                {
                    results[index] = item;
                    continue;
                }

                var recognizer = kind == WhitelistSubjectKind.Face
                    ? _faceRecognizer
                    : _catRecognizer;
                if (recognizer is null)
                {
                    results[index] = item with
                    {
                        IdentityName = null,
                        IsKnown = false,
                        RecognitionDistance = null
                    };
                    continue;
                }

                using var sample = PrepareSample(bgrFrame, item.Box, kind);
                if (sample is null)
                {
                    results[index] = item;
                    continue;
                }

                recognizer.Predict(sample, out var numericId, out var distance);
                var threshold = kind == WhitelistSubjectKind.Face
                    ? _faceThreshold
                    : _catThreshold;
                var profile = distance <= threshold
                    ? _profiles.FirstOrDefault(candidate =>
                        candidate.NumericId == numericId && candidate.Kind == kind)
                    : null;

                results[index] = item with
                {
                    IdentityName = profile?.Name,
                    IsKnown = profile is not null,
                    RecognitionDistance = distance
                };
            }

            return results;
        }
    }

    public WhitelistEnrollmentResult Enroll(
        Mat bgrFrame,
        TrackedObject target,
        string name,
        WhitelistSubjectKind kind)
        => Enroll(bgrFrame, target.Box, name, kind);

    public WhitelistEnrollmentResult Enroll(
        Mat bgrFrame,
        Rect region,
        string name,
        WhitelistSubjectKind kind)
    {
        var normalizedName = name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return new WhitelistEnrollmentResult(WhitelistEnrollmentStatus.InvalidName);
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            using var sample = PrepareSample(bgrFrame, region, kind);
            if (sample is null)
            {
                return new WhitelistEnrollmentResult(WhitelistEnrollmentStatus.NoMatchingTarget);
            }

            var profile = _profiles.FirstOrDefault(candidate =>
                candidate.Kind == kind &&
                string.Equals(candidate.Name, normalizedName, StringComparison.CurrentCultureIgnoreCase));
            if (profile is null)
            {
                profile = new StoredProfile
                {
                    Id = Guid.NewGuid().ToString("N"),
                    NumericId = _profiles.Count == 0 ? 1 : _profiles.Max(item => item.NumericId) + 1,
                    Name = normalizedName,
                    Kind = kind,
                    CreatedAt = DateTimeOffset.Now
                };
                _profiles.Add(profile);
            }

            var profileDirectory = GetProfileDirectory(profile.Id);
            Directory.CreateDirectory(profileDirectory);
            var samplePath = Path.Combine(profileDirectory, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.png");
            Cv2.ImWrite(samplePath, sample);
            SaveProfiles();
            RebuildModels();
            return new WhitelistEnrollmentResult(
                WhitelistEnrollmentStatus.Success,
                ToPublicProfile(profile));
        }
    }

    public bool Delete(string profileId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var profile = _profiles.FirstOrDefault(item => item.Id == profileId);
            if (profile is null)
            {
                return false;
            }

            _profiles.Remove(profile);
            var profileDirectory = GetProfileDirectory(profile.Id);
            if (Directory.Exists(profileDirectory))
            {
                Directory.Delete(profileDirectory, recursive: true);
            }

            SaveProfiles();
            RebuildModels();
            return true;
        }
    }

    private void RebuildModels()
    {
        _faceRecognizer?.Dispose();
        _catRecognizer?.Dispose();
        _faceRecognizer = BuildModel(WhitelistSubjectKind.Face);
        _catRecognizer = BuildModel(WhitelistSubjectKind.Cat);
    }

    private LBPHFaceRecognizer? BuildModel(WhitelistSubjectKind kind)
    {
        var images = new List<Mat>();
        var labels = new List<int>();
        try
        {
            foreach (var profile in _profiles.Where(item => item.Kind == kind))
            {
                var directory = GetProfileDirectory(profile.Id);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var path in Directory.EnumerateFiles(directory, "*.png"))
                {
                    var image = Cv2.ImRead(path, ImreadModes.Grayscale);
                    if (image.Empty())
                    {
                        image.Dispose();
                        continue;
                    }

                    images.Add(image);
                    labels.Add(profile.NumericId);
                }
            }

            if (images.Count == 0)
            {
                return null;
            }

            var recognizer = LBPHFaceRecognizer.Create(1, 8, 8, 8, double.MaxValue);
            recognizer.Train(images, labels);
            return recognizer;
        }
        finally
        {
            foreach (var image in images)
            {
                image.Dispose();
            }
        }
    }

    private static Mat? PrepareSample(Mat frame, Rect detectedBox, WhitelistSubjectKind kind)
    {
        var frameBounds = new Rect(0, 0, frame.Width, frame.Height);
        var box = detectedBox & frameBounds;
        if (box.Width < 24 || box.Height < 24)
        {
            return null;
        }

        if (kind == WhitelistSubjectKind.Cat)
        {
            var insetX = (int)MathF.Round(box.Width * 0.08f);
            box = new Rect(
                box.X + insetX,
                box.Y,
                Math.Max(1, box.Width - insetX * 2),
                Math.Max(1, (int)MathF.Round(box.Height * 0.62f))) & frameBounds;
        }

        using var roi = new Mat(frame, box);
        using var gray = new Mat();
        var conversion = roi.Channels() == 4
            ? ColorConversionCodes.BGRA2GRAY
            : ColorConversionCodes.BGR2GRAY;
        Cv2.CvtColor(roi, gray, conversion);

        var normalized = new Mat();
        Cv2.Resize(
            gray,
            normalized,
            new Size(NormalizedSize, NormalizedSize),
            interpolation: InterpolationFlags.Area);
        Cv2.EqualizeHist(normalized, normalized);
        return normalized;
    }

    private static bool TryGetKind(string label, out WhitelistSubjectKind kind)
    {
        if (string.Equals(label, "face", StringComparison.OrdinalIgnoreCase))
        {
            kind = WhitelistSubjectKind.Face;
            return true;
        }

        if (string.Equals(label, "cat", StringComparison.OrdinalIgnoreCase))
        {
            kind = WhitelistSubjectKind.Cat;
            return true;
        }

        kind = default;
        return false;
    }

    private List<StoredProfile> LoadProfiles()
    {
        try
        {
            if (!File.Exists(_profilesPath))
            {
                return [];
            }

            var profiles = JsonSerializer.Deserialize<List<StoredProfile>>(
                File.ReadAllText(_profilesPath),
                _jsonOptions) ?? [];
            return profiles
                .Where(profile =>
                    Guid.TryParseExact(profile.Id, "N", out _) &&
                    profile.NumericId > 0 &&
                    !string.IsNullOrWhiteSpace(profile.Name))
                .GroupBy(profile => profile.Id)
                .Select(group => group.First())
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private void SaveProfiles()
    {
        Directory.CreateDirectory(_storageDirectory);
        var temporaryPath = _profilesPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(_profiles, _jsonOptions));
        File.Move(temporaryPath, _profilesPath, overwrite: true);
    }

    private WhitelistProfile ToPublicProfile(StoredProfile profile)
    {
        var directory = GetProfileDirectory(profile.Id);
        var sampleCount = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.png").Count()
            : 0;
        return new WhitelistProfile(
            profile.Id,
            profile.NumericId,
            profile.Name,
            profile.Kind,
            profile.CreatedAt,
            sampleCount);
    }

    private string GetProfileDirectory(string profileId) =>
        Path.Combine(_samplesDirectory, profileId);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _faceRecognizer?.Dispose();
            _catRecognizer?.Dispose();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private sealed class StoredProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public int NumericId { get; set; }
        public string Name { get; set; } = string.Empty;
        public WhitelistSubjectKind Kind { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    }
}
