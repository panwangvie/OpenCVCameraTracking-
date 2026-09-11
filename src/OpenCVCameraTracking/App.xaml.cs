using System.Windows;
using OpenCVCameraTracking.Configuration;
using OpenCVCameraTracking.Core.Logging;
using OpenCVCameraTracking.Localization;

namespace OpenCVCameraTracking;

public partial class App : Application
{
    public ApplicationSettings Settings { get; set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        AppLogger.Initialize();
        AppLogger.Info("Application startup");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        Settings = SettingsStore.Load();
        AppLogger.Info($"Settings loaded: language={Settings.Language}, sourceKind={Settings.SelectedSourceKind}");
        LocalizationManager.Apply(Settings.Language);
        base.OnStartup(e);
        new MainWindow().Show();
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("Unhandled dispatcher exception", e.Exception);
        e.Handled = true;
        MessageBox.Show(
            "程序遇到未处理的界面异常，详细信息已记录到本地日志。\n\n" + e.Exception.Message,
            "OpenCVCameraTracking",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppLogger.Error("Unhandled AppDomain exception", exception);
        }
    }
}
