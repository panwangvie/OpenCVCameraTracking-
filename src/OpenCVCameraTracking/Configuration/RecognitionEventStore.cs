using System.IO;
using System.Text.Json;
using OpenCVCameraTracking.Core.Recognition;

namespace OpenCVCameraTracking.Configuration;

public sealed record RecognitionEventRecord(
    DateTimeOffset Timestamp,
    WhitelistSubjectKind Kind,
    string? IdentityName,
    bool IsKnown,
    int TrackId,
    double? Distance);

public sealed class RecognitionEventStore
{
    private readonly object _gate = new();

    public string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenCVCameraTracking",
        "recognition-events.jsonl");

    public void Append(RecognitionEventRecord record)
    {
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(directory);
            File.AppendAllText(LogPath, JsonSerializer.Serialize(record) + Environment.NewLine);
        }
    }
}
