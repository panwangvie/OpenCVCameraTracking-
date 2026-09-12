using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCVCameraTracking.Core.Detection;
using OpenCVCameraTracking.Core.Logging;
using OpenCvSharp;
using OpenCvSharp.Face;

namespace OpenCVCameraTracking.Core.Recognition;

/// <summary>
/// Local whitelist recognition. Human faces use aligned SFace embeddings and
/// cosine similarity; cats retain the lightweight LBPH appearance matcher.
/// Decisions are stabilized with a per-track voting window and asymmetric
/// enter/exit thresholds, so a marginal frame cannot immediately flip a result.
/// </summary>
public sealed class WhitelistRecognitionService : IDisposable
{
    private const int CatNormalizedSize = 128;
    private const int VoteWindowSize = 7;
    private const int KnownEnterVotes = 3;
    private const int UnknownEnterVotes = 5;
    private const double FaceExitThresholdOffset = 0.04d;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly string _storageDirectory;
    private readonly string _samplesDirectory;
    private readonly string _profilesPath;
    private readonly double _faceSimilarityThreshold;
    private readonly double _catThreshold;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SFaceEmbeddingExtractor? _faceEmbeddingExtractor;
    private readonly List<FaceTemplate> _faceTemplates = [];
    private readonly Dictionary<int, Queue<RecognitionVote>> _trackVotes = [];
    private readonly Dictionary<int, StableRecognitionState> _stableTrackStates = [];

    private List<StoredProfile> _profiles;
    private LBPHFaceRecognizer? _catRecognizer;
    private bool _disposed;

    public WhitelistRecognitionService(
        string? storageDirectory = null,
        string? faceRecognitionModelPath = null,
        double faceSimilarityThreshold = 0.363d,
        double catThreshold = 52d)
    {
        _storageDirectory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenCVCameraTracking",
            "Whitelist");
        _samplesDirectory = Path.Combine(_storageDirectory, "samples");
        _profilesPath = Path.Combine(_storageDirectory, "profiles.json");
        _faceSimilarityThreshold = faceSimilarityThreshold;
        _catThreshold = catThreshold;

        var modelPath = faceRecognitionModelPath ?? Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Models",
            "face_recognition_sface_2021dec.onnx");
        if (File.Exists(modelPath))
        {
            _faceEmbeddingExtractor = new SFaceEmbeddingExtractor(modelPath);
        }

        Directory.CreateDirectory(_samplesDirectory);
        _profiles = LoadProfiles();
        RebuildModels();
        AppLogger.Info($"Whitelist service initialized: profiles={_profiles.Count}, faceTemplates={_faceTemplates.Count}");
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

    public IReadOnlyList<TrackedObject> Recognize(Mat bgrFrame, IReadOnlyList<TrackedObject> objects)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var activeTrackIds = objects.Select(item => item.Id).ToHashSet();
            foreach (var inactiveTrackId in _trackVotes.Keys.Where(id => !activeTrackIds.Contains(id)).ToArray())
            {
                _trackVotes.Remove(inactiveTrackId);
                _stableTrackStates.Remove(inactiveTrackId);
            }

            var results = new TrackedObject[objects.Count];
            for (var index = 0; index < objects.Count; index++)
            {
                var item = objects[index];
                if (!TryGetKind(item.Label, out var kind))
                {
                    results[index] = item;
                    continue;
                }

                // With no enrolled profile there is no basis for an "unknown"
                // decision. Keep the target pending instead of alarming the user.
                if (!_profiles.Any(profile => profile.Kind == kind))
                {
                    results[index] = item with
                    {
                        IdentityName = null,
                        IsKnown = null,
                        RecognitionDistance = null,
                        RecognitionSimilarity = null
                    };
                    continue;
                }

                var observation = kind == WhitelistSubjectKind.Face
                    ? RecognizeFace(bgrFrame, item)
                    : RecognizeCat(bgrFrame, item);
                results[index] = Stabilize(item, observation);
            }

            return results;
        }
    }

    public void ResetTracking()
    {
        lock (_gate)
        {
            _trackVotes.Clear();
            _stableTrackStates.Clear();
        }
    }

    public Task<WhitelistEnrollmentResult> EnrollAsync(
        Mat bgrFrame,
        TrackedObject target,
        string name,
        WhitelistSubjectKind kind) =>
        EnrollAsync(bgrFrame, target.Box, target.Landmarks, name, kind);

    public Task<WhitelistEnrollmentResult> EnrollAsync(
        Mat bgrFrame,
        Rect region,
        string name,
        WhitelistSubjectKind kind) =>
        EnrollAsync(bgrFrame, region, null, name, kind);

    private Task<WhitelistEnrollmentResult> EnrollAsync(
        Mat bgrFrame,
        Rect region,
        IReadOnlyList<Point2f>? landmarks,
        string name,
        WhitelistSubjectKind kind) =>
        Task.Run(() => EnrollCoreAsync(bgrFrame, region, landmarks, name, kind));

    private async Task<WhitelistEnrollmentResult> EnrollCoreAsync(
        Mat bgrFrame,
        Rect region,
        IReadOnlyList<Point2f>? landmarks,
        string name,
        WhitelistSubjectKind kind)
    {
        var normalizedName = name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return new WhitelistEnrollmentResult(WhitelistEnrollmentStatus.InvalidName);
        }

        // Enrollment writes images and rebuilds native recognition models. The
        // caller performs this work on a background thread; the mutation gate
        // keeps it mutually exclusive with an asynchronous deletion without
        // holding a monitor lock across any asynchronous operation.
        await _mutationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                using var sample = PrepareEnrollmentSample(bgrFrame, region, landmarks, kind);
                if (sample is null || sample.Empty())
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
                AppLogger.Info($"Whitelist enrollment succeeded: kind={kind}, profile={profile.Name}, samples={ToPublicProfile(profile).SampleCount}");
                return new WhitelistEnrollmentResult(
                    WhitelistEnrollmentStatus.Success,
                    ToPublicProfile(profile));
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public Task<bool> DeleteAsync(string profileId) =>
        Task.Run(() => DeleteCoreAsync(profileId));

    private async Task<bool> DeleteCoreAsync(string profileId)
    {
        await _mutationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            StoredProfile? profile;
            lock (_gate)
            {
                ThrowIfDisposed();
                profile = _profiles.FirstOrDefault(item => item.Id == profileId);
            }

            if (profile is null)
            {
                return false;
            }

            var profileDirectory = GetProfileDirectory(profile.Id);
            if (Directory.Exists(profileDirectory))
            {
                await DeleteDirectoryWithRetryAsync(profileDirectory).ConfigureAwait(false);
            }

            lock (_gate)
            {
                ThrowIfDisposed();
                var currentProfile = _profiles.FirstOrDefault(item => item.Id == profileId);
                if (currentProfile is null)
                {
                    return false;
                }

                _profiles.Remove(currentProfile);
                SaveProfiles();
                RebuildModels();
                AppLogger.Info($"Whitelist profile deleted: kind={currentProfile.Kind}, profile={currentProfile.Name}");
                return true;
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private static async Task DeleteDirectoryWithRetryAsync(string directory)
    {
        const int maximumAttempts = 5;
        IOException? lastException = null;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException exception) when (attempt < maximumAttempts)
            {
                lastException = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(120 * attempt)).ConfigureAwait(false);
            }
        }

        throw lastException ?? new IOException($"Unable to delete whitelist directory '{directory}'.");
    }

    private RecognitionObservation RecognizeFace(Mat frame, TrackedObject item)
    {
        if (_faceEmbeddingExtractor is null)
        {
            return new RecognitionObservation(null, false, null, null);
        }

        using var aligned = _faceEmbeddingExtractor.AlignFace(frame, item.Box, item.Landmarks);
        var embedding = _faceEmbeddingExtractor.Extract(aligned);
        if (embedding.Length == 0)
        {
            return new RecognitionObservation(null, false, null, null);
        }

        FaceTemplate? bestTemplate = null;
        var bestSimilarity = -1d;
        foreach (var template in _faceTemplates)
        {
            var similarity = SFaceEmbeddingExtractor.CosineSimilarity(embedding, template.Embedding);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestTemplate = template;
            }
        }

        var displaySimilarity = Math.Clamp(bestSimilarity, 0d, 1d);
        // Keep the best candidate even below the entry threshold. Stabilize can then
        // apply a lower exit threshold for an already-recognised, same identity.
        return new RecognitionObservation(
            bestTemplate?.Profile,
            bestTemplate is not null && bestSimilarity >= _faceSimilarityThreshold,
            displaySimilarity,
            null);
    }

    private RecognitionObservation RecognizeCat(Mat frame, TrackedObject item)
    {
        if (_catRecognizer is null)
        {
            return new RecognitionObservation(null, false, null, null);
        }

        using var sample = PrepareCatSample(frame, item.Box);
        if (sample is null)
        {
            return new RecognitionObservation(null, false, null, null);
        }

        _catRecognizer.Predict(sample, out var numericId, out var distance);
        var profile = distance <= _catThreshold
            ? _profiles.FirstOrDefault(candidate =>
                candidate.NumericId == numericId && candidate.Kind == WhitelistSubjectKind.Cat)
            : null;
        return new RecognitionObservation(profile, profile is not null, null, distance);
    }

    private TrackedObject Stabilize(TrackedObject item, RecognitionObservation observation)
    {
        // Tracks retained briefly after a missed detection contain an old rectangle.
        // Do not run recognition or emit a new state from that stale image region.
        if (item.Misses > 0)
        {
            return item with { IdentityName = null, IsKnown = null };
        }

        if (!_trackVotes.TryGetValue(item.Id, out var votes))
        {
            votes = new Queue<RecognitionVote>();
            _trackVotes[item.Id] = votes;
        }

        var isMatch = observation.IsMatch;
        if (_stableTrackStates.TryGetValue(item.Id, out var previousStable) &&
            previousStable.IsKnown &&
            string.Equals(previousStable.ProfileId, observation.Profile?.Id, StringComparison.Ordinal) &&
            observation.Similarity is { } similarity &&
            similarity >= Math.Max(0d, _faceSimilarityThreshold - FaceExitThresholdOffset))
        {
            isMatch = true;
        }

        votes.Enqueue(new RecognitionVote(
            observation.Profile?.Id,
            observation.Profile?.Name,
            isMatch,
            observation.Similarity,
            observation.Distance));
        while (votes.Count > VoteWindowSize)
        {
            votes.Dequeue();
        }

        var knownGroup = votes
            .Where(vote => vote.IsMatch && vote.ProfileId is not null)
            .GroupBy(vote => vote.ProfileId)
            .Select(group => new { Votes = group.ToArray(), Count = group.Count() })
            .OrderByDescending(group => group.Count)
            .FirstOrDefault();
        if (knownGroup is { Count: >= KnownEnterVotes })
        {
            var latest = knownGroup.Votes[^1];
            var stable = new StableRecognitionState(true, latest.ProfileId, latest.ProfileName);
            _stableTrackStates[item.Id] = stable;
            return ApplyStableState(item, stable, knownGroup.Votes);
        }

        var unknownVotes = votes.Where(vote => !vote.IsMatch).ToArray();
        if (unknownVotes.Length >= UnknownEnterVotes)
        {
            var stable = new StableRecognitionState(false, null, null);
            _stableTrackStates[item.Id] = stable;
            return ApplyStableState(item, stable, unknownVotes);
        }

        // Hysteresis: retain a previously confirmed decision while evidence is
        // inconclusive. A new decision must meet its own entry vote threshold.
        if (_stableTrackStates.TryGetValue(item.Id, out var stableState))
        {
            return ApplyStableState(item, stableState, votes);
        }

        return item with
        {
            IdentityName = null,
            IsKnown = null,
            RecognitionSimilarity = observation.Similarity,
            RecognitionDistance = observation.Distance
        };
    }

    private static TrackedObject ApplyStableState(
        TrackedObject item,
        StableRecognitionState stable,
        IEnumerable<RecognitionVote> evidence)
    {
        var evidenceArray = evidence.ToArray();
        return item with
        {
            IdentityName = stable.ProfileName,
            IsKnown = stable.IsKnown,
            RecognitionSimilarity = AverageNullable(evidenceArray.Select(vote => vote.Similarity)),
            RecognitionDistance = AverageNullable(evidenceArray.Select(vote => vote.Distance))
        };
    }

    private void RebuildModels()
    {
        _faceTemplates.Clear();
        BuildFaceTemplates();
        _catRecognizer?.Dispose();
        _catRecognizer = BuildCatModel();
        _trackVotes.Clear();
        _stableTrackStates.Clear();
    }

    private void BuildFaceTemplates()
    {
        if (_faceEmbeddingExtractor is null)
        {
            return;
        }

        foreach (var profile in _profiles.Where(item => item.Kind == WhitelistSubjectKind.Face))
        {
            var directory = GetProfileDirectory(profile.Id);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*.png"))
            {
                using var image = Cv2.ImRead(path, ImreadModes.Color);
                if (image.Empty())
                {
                    continue;
                }

                var embedding = _faceEmbeddingExtractor.Extract(image);
                if (embedding.Length > 0)
                {
                    _faceTemplates.Add(new FaceTemplate(profile, embedding));
                }
            }
        }
    }

    private LBPHFaceRecognizer? BuildCatModel()
    {
        var images = new List<Mat>();
        var labels = new List<int>();
        try
        {
            foreach (var profile in _profiles.Where(item => item.Kind == WhitelistSubjectKind.Cat))
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

    private Mat? PrepareEnrollmentSample(
        Mat frame,
        Rect region,
        IReadOnlyList<Point2f>? landmarks,
        WhitelistSubjectKind kind)
    {
        if (kind == WhitelistSubjectKind.Face)
        {
            if (_faceEmbeddingExtractor is null)
            {
                return null;
            }

            var aligned = _faceEmbeddingExtractor.AlignFace(frame, region, landmarks);
            if (aligned.Empty())
            {
                aligned.Dispose();
                return null;
            }

            return aligned;
        }

        return PrepareCatSample(frame, region);
    }

    private static Mat? PrepareCatSample(Mat frame, Rect detectedBox)
    {
        var frameBounds = new Rect(0, 0, frame.Width, frame.Height);
        var box = detectedBox & frameBounds;
        if (box.Width < 24 || box.Height < 24)
        {
            return null;
        }

        var insetX = (int)MathF.Round(box.Width * 0.08f);
        box = new Rect(
            box.X + insetX,
            box.Y,
            Math.Max(1, box.Width - insetX * 2),
            Math.Max(1, (int)MathF.Round(box.Height * 0.62f))) & frameBounds;

        using var roi = new Mat(frame, box);
        using var gray = new Mat();
        var conversion = roi.Channels() == 4
            ? ColorConversionCodes.BGRA2GRAY
            : ColorConversionCodes.BGR2GRAY;
        Cv2.CvtColor(roi, gray, conversion);

        var normalized = new Mat();
        Cv2.Resize(gray, normalized, new Size(CatNormalizedSize, CatNormalizedSize), interpolation: InterpolationFlags.Area);
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
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_profiles, _jsonOptions));
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

    private string GetProfileDirectory(string profileId) => Path.Combine(_samplesDirectory, profileId);

    private static double? AverageNullable(IEnumerable<double?> values)
    {
        var available = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return available.Length == 0 ? null : available.Average();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _faceEmbeddingExtractor?.Dispose();
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

    private sealed record FaceTemplate(StoredProfile Profile, float[] Embedding);
    private sealed record RecognitionObservation(StoredProfile? Profile, bool IsMatch, double? Similarity, double? Distance);
    private sealed record RecognitionVote(string? ProfileId, string? ProfileName, bool IsMatch, double? Similarity, double? Distance);
    private sealed record StableRecognitionState(bool IsKnown, string? ProfileId, string? ProfileName);
}
