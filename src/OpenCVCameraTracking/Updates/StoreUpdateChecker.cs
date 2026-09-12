using System.Net.Http.Json;
using System.Net.Http;
using System.Reflection;
using OpenCVCameraTracking.Core.Logging;

namespace OpenCVCameraTracking.Updates;

public sealed record StoreUpdateInfo(Version CurrentVersion, Version AvailableVersion, string StoreUrl);

/// <summary>Checks the public release manifest used for the Microsoft Store listing.</summary>
public sealed class StoreUpdateChecker
{
    private const string UpdateManifestUrl = "https://raw.githubusercontent.com/wutangyuan/OpenCVCameraTracking-/main/update-manifest.json";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(6) };

    public async Task<StoreUpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = GetCurrentVersion();
            var manifest = await HttpClient.GetFromJsonAsync<UpdateManifest>(UpdateManifestUrl, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (manifest is null || !Version.TryParse(manifest.Version, out var available) || available <= current)
            {
                return null;
            }

            var storeUrl = string.IsNullOrWhiteSpace(manifest.StoreUrl)
                ? "ms-windows-store://search/?query=CameraTracking"
                : manifest.StoreUrl;
            return new StoreUpdateInfo(current, available, storeUrl);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Store services are unavailable for unpackaged/dev runs and must not
            // make the camera application fail to start.
            AppLogger.Warn($"Microsoft Store update check skipped: {exception.Message}");
            return null;
        }
    }

    public static Version GetCurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version ?? new Version(1, 0, 0, 0);
    }

    private sealed record UpdateManifest(string Version, string StoreUrl);
}
