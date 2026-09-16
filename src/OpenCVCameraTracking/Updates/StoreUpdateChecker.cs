using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
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
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;
    private const string StoreSearchUrl = "ms-windows-store://search/?query=CameraTracking";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int GetCurrentPackagePath(ref uint packagePathLength, StringBuilder? packagePath);

    public async Task<StoreUpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!HasPackageIdentity())
        {
            AppLogger.Info("Microsoft Store update check skipped because the app has no package identity.");
            return null;
        }

        var current = GetCurrentVersion();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private static Version? TryGetCurrentPackageVersion()
    {
        // Unpackaged launches do not have Package.Current.  Check the package
        // identity through the AppModel API before accessing it.
        if (!HasPackageIdentity())
        {
            return null;
        }

        try
        {
            return ToVersion(Package.Current.Id.Version);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool HasPackageIdentity()
    {
        uint pathLength = 0;
        var result = GetCurrentPackagePath(ref pathLength, null);

        // A packaged process returns ERROR_INSUFFICIENT_BUFFER for the size-only
        // query.  An unpackaged process returns APPMODEL_ERROR_NO_PACKAGE.
        if (result == AppModelErrorNoPackage)
        {
            return false;
        }

        return result == ErrorInsufficientBuffer || result == 0;
    }

    private static Version ToVersion(PackageVersion version) =>
        new(version.Major, version.Minor, version.Build, version.Revision);
}
