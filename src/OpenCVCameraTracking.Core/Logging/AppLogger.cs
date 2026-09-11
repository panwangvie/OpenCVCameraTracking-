using log4net;
using log4net.Appender;
using log4net.Layout;
using log4net.Config;
using System.Reflection;

namespace OpenCVCameraTracking.Core.Logging;

/// <summary>Centralized, privacy-conscious application logging.</summary>
public static class AppLogger
{
    private static readonly ILog Log = LogManager.GetLogger("OpenCVCameraTracking");
    private static int _initialized;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenCVCameraTracking",
            "Logs");
        Directory.CreateDirectory(logDirectory);
        var repository = LogManager.GetRepository(Assembly.GetEntryAssembly() ?? typeof(AppLogger).Assembly);
        var appender = new RollingFileAppender
        {
            Name = "RollingFile",
            File = Path.Combine(logDirectory, "application.log"),
            AppendToFile = true,
            RollingStyle = RollingFileAppender.RollingMode.Date,
            DatePattern = ".yyyy-MM-dd",
            StaticLogFileName = true,
            MaxSizeRollBackups = 14,
            MaximumFileSize = "10MB",
            LockingModel = new FileAppender.MinimalLock(),
            Layout = new PatternLayout("%date{yyyy-MM-dd HH:mm:ss.fff} [%thread] %-5level %logger - %message%newline")
        };
        appender.ActivateOptions();
        BasicConfigurator.Configure(repository, appender);

        Info("Logging initialized");
    }

    public static void Info(string message) => Log.Info(message);
    public static void Warn(string message) => Log.Warn(message);
    public static void Error(string message, Exception? exception = null)
    {
        if (exception is null) Log.Error(message); else Log.Error(message, exception);
    }
}
