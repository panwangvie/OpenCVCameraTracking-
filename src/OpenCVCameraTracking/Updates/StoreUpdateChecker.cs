using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.Services.Store;
using OpenCVCameraTracking.Core.Logging;

namespace OpenCVCameraTracking.Updates;

public sealed record StoreUpdateInfo(Version CurrentVersion, Version AvailableVersion, string StoreUrl);

/// <summary>
/// Checks the Microsoft Store for package updates that are actually available to the
/// current signed-in account. This must be called from the application's UI thread.
/// </summary>
public sealed class StoreUpdateChecker
{
    private const string StoreSearchUrl = "ms-windows-store://search/?query=CameraTracking";

    public async Task<StoreUpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!HasPackageIdentity())
        {
            // F5/debug and portable runs cannot query a Store entitlement.
            AppLogger.Info("Microsoft Store update check skipped because the app has no package identity.");
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = GetCurrentVersion();
            var context = StoreContext.GetDefault();

            // The Store API is the source of truth: a package in Partner Center that is
            // still processing or in certification is not returned here.
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync().AsTask();
            cancellationToken.ThrowIfCancellationRequested();

            Version? available = null;
            foreach (var update in updates)
            {
                var package = update.Package;
                if (package is null)
                {
                    continue;
                }

                var version = ToVersion(package.Id.Version);
                if (version > current && (available is null || version > available))
                {
                    available = version;
                }
            }

            return available is null ? null : new StoreUpdateInfo(current, available, StoreSearchUrl);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Store sign-in, connectivity and availability must not affect camera use.
            AppLogger.Warn($"Microsoft Store update check skipped: {exception.Message}");
            return null;
        }
    }

    public static Version GetCurrentVersion()
    {
        var packageVersion = TryGetCurrentPackageVersion();
        if (packageVersion is not null)
        {
            return packageVersion;
        }

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version ?? new Version(1, 0, 0, 0);
    }

    private static bool HasPackageIdentity()
    {
        return TryGetCurrentPackageVersion() is not null;
    }

    private static Version? TryGetCurrentPackageVersion()
    {
        try
        {
            return ToVersion(Package.Current.Id.Version);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Version ToVersion(PackageVersion version) =>
        new(version.Major, version.Minor, version.Build, version.Revision);
}
