using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CvRect = OpenCvSharp.Rect;
using OpenCVCameraTracking.Core;
using OpenCVCameraTracking.Core.Camera;
using OpenCVCameraTracking.Core.Detection;
using OpenCVCameraTracking.Core.Recognition;
using OpenCVCameraTracking.Core.Tracking;
using OpenCVCameraTracking.Core.Logging;
using OpenCVCameraTracking.Configuration;
using OpenCVCameraTracking.Localization;
using Microsoft.Win32;
using OpenCVCameraTracking.Updates;
using System.Diagnostics;

namespace OpenCVCameraTracking;

public partial class MainWindow : Window
{
    private ApplicationSettings _settings;
    private readonly WhitelistRecognitionService _whitelistRecognition = new();
    private readonly RecognitionEventStore _recognitionEventStore = new();
    private readonly Dictionary<int, string> _recognitionStates = [];
    private readonly List<RecentRecognitionEvent> _recentRecognitionEvents = [];
    private readonly StoreUpdateChecker _storeUpdateChecker = new();
    private StoreUpdateInfo? _pendingStoreUpdate;
    private readonly DispatcherTimer _storeUpdateTimer;
    private bool _storeUpdateCheckRunning;
    private readonly DispatcherTimer _unknownAlertTimer;
    private CameraTrackingEngine? _engine;
    private WriteableBitmap? _previewBitmap;
    private int _renderPending;
    private bool _isClosing;
    private bool _applyingSettings;
    private int _latestFrameWidth;
    private int _latestFrameHeight;

    public MainWindow()
    {
        InitializeComponent();
        _unknownAlertTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _unknownAlertTimer.Tick += (_, _) =>
        {
            _unknownAlertTimer.Stop();
            UnknownAlertBanner.Visibility = Visibility.Collapsed;
        };
        _storeUpdateTimer = new DispatcherTimer
        {
            // Keep the running app aware of Store releases without polling too often.
            Interval = TimeSpan.FromMinutes(20)
        };
        _storeUpdateTimer.Tick += StoreUpdateTimerOnTick;
        _settings = ((App)Application.Current).Settings;
        ApplySettingsToUi();
        RefreshVersionDisplay();
        Loaded += MainWindowOnLoaded;
    }

    private async void MainWindowOnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindowOnLoaded;
        await RefreshDevicesAsync();
        await CheckForStoreUpdateAsync();
        _storeUpdateTimer.Start();
    }

    private async void StoreUpdateTimerOnTick(object? sender, EventArgs e)
    {
        await CheckForStoreUpdateAsync();
    }

    private async Task CheckForStoreUpdateAsync()
    {
        if (_isClosing || _storeUpdateCheckRunning)
        {
            return;
        }

        _storeUpdateCheckRunning = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            StoreUpdateInfo? update;
            try
            {
                update = await _storeUpdateChecker.CheckAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                // A slow/offline network must never interrupt the camera UI.
                return;
            }

            if (update is null)
            {
                return;
            }

            if (IsUpdateDismissed(update))
            {
                return;
            }

            var isNewUpdate = _pendingStoreUpdate is null ||
                              update.AvailableVersion > _pendingStoreUpdate.AvailableVersion;
            _pendingStoreUpdate = update;
            RefreshVersionDisplay(update);
            UpdateBanner.Visibility = Visibility.Visible;
            if (isNewUpdate)
            {
                AppLogger.Info($"Microsoft Store update available: current={update.CurrentVersion}, available={update.AvailableVersion}");
            }
        }
        finally
        {
            _storeUpdateCheckRunning = false;
        }
    }

    private void RefreshVersionDisplay(StoreUpdateInfo? update = null)
    {
        var version = update?.CurrentVersion ?? StoreUpdateChecker.GetCurrentVersion();
        VersionText.Text = LocalizationManager.Format("VersionLabel", version.ToString(4));
        if (update is not null)
        {
            UpdateVersionText.Text = LocalizationManager.Format("VersionLabel", update.AvailableVersion.ToString(4));
        }
    }

    private void OpenStoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _pendingStoreUpdate?.StoreUrl ?? "ms-windows-store://search/?query=CameraTracking",
                UseShellExecute = true
            });
            AppLogger.Info("User opened Microsoft Store update link");
        }
        catch (Exception exception)
        {
            AppLogger.Error("Unable to open Microsoft Store", exception);
        }
    }

    private void DismissUpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_pendingStoreUpdate is null)
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
            return;
        }

        _settings.DismissedStoreUpdateVersion = _pendingStoreUpdate.AvailableVersion.ToString(4);
        SaveSettings();
        UpdateBanner.Visibility = Visibility.Collapsed;
        AppLogger.Info($"User dismissed Microsoft Store update: version={_settings.DismissedStoreUpdateVersion}");
    }

    private bool IsUpdateDismissed(StoreUpdateInfo update) =>
        string.Equals(
            _settings.DismissedStoreUpdateVersion,
            update.AvailableVersion.ToString(4),
            StringComparison.OrdinalIgnoreCase);

    private async Task RefreshDevicesAsync()
    {
        StatusText.Text = LocalizationManager.Get("Status_Enumerating");
        try
        {
            var devices = await Task.Run(DirectShowCameraEnumerator.GetVideoInputDevices);
            DeviceBox.ItemsSource = devices;
            DeviceBox.SelectedIndex = devices.Count > 0 ? 0 : -1;
            StatusText.Text = devices.Count > 0
                ? LocalizationManager.Format("Status_FoundCameras", devices.Count)
                : LocalizationManager.Get("Status_NoCamera");
        }
        catch (Exception exception)
        {
            StatusText.Text = LocalizationManager.Format("Status_EnumerationFailed", exception.Message);
        }
    }

    private async void StartButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLogger.Info("User clicked start tracking");
            await StopEngineAsync();
            PersistUiSelection();
            var detector = CreateDetector();
            var tracker = new IouMultiObjectTracker(minimumIou: 0.18f, maximumMisses: 8, smoothing: 0.72f);
            _recognitionStates.Clear();
            _recentRecognitionEvents.Clear();
            _engine = new CameraTrackingEngine(
                detector,
                detectionInterval: 1,
                tracker,
                _whitelistRecognition);
            _engine.FrameReady += EngineOnFrameReady;
            _engine.StatusChanged += EngineOnStatusChanged;
            _engine.Faulted += EngineOnFaulted;

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            SetConfigurationEnabled(false);
            StatusText.Text = LocalizationManager.Get("Status_Connecting");
            await _engine.StartAsync(CreateSourceOptions());
        }
        catch (Exception exception)
        {
            AppLogger.Error("Unable to start tracking", exception);
            await StopEngineAsync();
            MessageBox.Show(
                this,
                exception.Message,
                LocalizationManager.Get("UnableToStart"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void StopButton_OnClick(object sender, RoutedEventArgs e) => await StopEngineAsync();

    private IObjectDetector CreateDetector()
    {
        var modelDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Models");
        return SelectedTag(DetectionModeBox) switch
        {
            "Face" => new YuNetFaceDetector(
                Path.Combine(modelDirectory, "face_detection_yunet_2023mar.onnx"),
                _settings.FaceConfidence),
            "Haar" => new HaarFaceDetector(
                Path.Combine(modelDirectory, "haarcascade_frontalface_default.xml")),
            "Animal" => CreateAnimalDetector(),
            "PersonAnimal" => new CompositeObjectDetector(
            [
                new YuNetFaceDetector(
                    Path.Combine(modelDirectory, "face_detection_yunet_2023mar.onnx"),
                    _settings.FaceConfidence),
                CreateAnimalDetector()
            ]),
            _ => throw new InvalidOperationException("Unknown detection mode.")
        };
    }

    private IObjectDetector CreateAnimalDetector()
    {
        if (SelectedTag(AnimalModelBox) == "BuiltIn")
        {
            var modelDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Models");
            return new YoloXOnnxDetector(
                Path.Combine(modelDirectory, "object_detection_yolox_2022nov_int8.onnx"),
                confidenceThreshold: _settings.AnimalConfidence);
        }

        return CreateCustomAnimalDetector();
    }

    private IObjectDetector CreateCustomAnimalDetector()
    {
        var modelPath = ModelPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new InvalidOperationException(LocalizationManager.Get("SelectModelError"));
        }

        return new YoloOnnxDetector(
            modelPath,
            labels: YoloOnnxDetector.CocoLabels,
            allowedLabels: YoloOnnxDetector.CocoAnimalLabels,
            confidenceThreshold: _settings.AnimalConfidence);
    }

    private CameraSourceOptions CreateSourceOptions()
    {
        var kind = SelectedTag(SourceKindBox);
        if (kind == "Device")
        {
            var selectedDevice = DeviceBox.SelectedItem as CameraDeviceInfo
                ?? throw new InvalidOperationException(LocalizationManager.Get("SelectCameraError"));
            return new CameraSourceOptions
            {
                Kind = CameraSourceKind.Device,
                DeviceIndex = selectedDevice.Index
            };
        }

        var address = AddressBox.Text.Trim();
        if (kind == "Stream" && !address.Contains("://", StringComparison.Ordinal))
        {
            address = "rtsp://" + address;
            AddressBox.Text = address;
        }

        return new CameraSourceOptions
        {
            Kind = kind == "File" ? CameraSourceKind.File : CameraSourceKind.Stream,
            Address = address,
            PreferTcpForRtsp = true,
            LowLatencyMode = _settings.RtspLowLatency,
            OpenTimeoutMilliseconds = 5_000,
            ReadTimeoutMilliseconds = 3_000
        };
    }

    private void EngineOnFrameReady(object? sender, FrameReadyEventArgs e)
    {
        if (Interlocked.Exchange(ref _renderPending, 1) == 1)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            try
            {
                // A frame may already be queued when Stop is clicked. Ignore
                // callbacks from that disposed engine so the preview stays blank.
                if (_isClosing || !ReferenceEquals(sender, _engine))
                {
                    return;
                }

                if (_previewBitmap is null ||
                    _previewBitmap.PixelWidth != e.Width ||
                    _previewBitmap.PixelHeight != e.Height)
                {
                    _previewBitmap = new WriteableBitmap(
                        e.Width,
                        e.Height,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null);
                    PreviewImage.Source = _previewBitmap;
                    PreviewPlaceholder.Visibility = Visibility.Collapsed;
                }

                _latestFrameWidth = e.Width;
                _latestFrameHeight = e.Height;

                _previewBitmap.WritePixels(
                    new Int32Rect(0, 0, e.Width, e.Height),
                    e.Pixels,
                    e.Stride,
                    0);
                MetricsText.Text = LocalizationManager.Format("MetricsFormat", e.FramesPerSecond, e.Objects.Count);
                HandleRecognitionEvents(e.Objects);
            }
            finally
            {
                Interlocked.Exchange(ref _renderPending, 0);
            }
        }, DispatcherPriority.Render);
    }

    private void EngineOnStatusChanged(object? sender, string statusCode) =>
        _ = Dispatcher.InvokeAsync(() => StatusText.Text = LocalizationManager.Get($"Status_{statusCode}"));

    private void EngineOnFaulted(object? sender, Exception exception) =>
        _ = Dispatcher.InvokeAsync(async () =>
        {
            AppLogger.Error("Tracking engine reported a fault", exception);
            var message = exception.InnerException?.Message ?? exception.Message;
            StatusText.Text = LocalizationManager.Format("Status_Error", message);
            if (!_isClosing)
            {
                MessageBox.Show(
                    this,
                    message,
                    LocalizationManager.Get("ProcessingError"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            await StopEngineAsync();
        });

    private async Task StopEngineAsync()
    {
        var engine = _engine;
        _engine = null;
        if (engine is not null)
        {
            AppLogger.Info("Stopping tracking engine");
            engine.FrameReady -= EngineOnFrameReady;
            engine.StatusChanged -= EngineOnStatusChanged;
            engine.Faulted -= EngineOnFaulted;
            await engine.DisposeAsync();
        }

        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        SetConfigurationEnabled(true);
        _previewBitmap = null;
        _latestFrameWidth = 0;
        _latestFrameHeight = 0;
        PreviewImage.Source = null;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        MetricsText.Text = LocalizationManager.Format("MetricsFormat", 0d, 0);
    }

    private void SetConfigurationEnabled(bool enabled)
    {
        SourceKindBox.IsEnabled = enabled;
        DeviceBox.IsEnabled = enabled;
        StreamProfileBox.IsEnabled = enabled;
        AddressBox.IsEnabled = enabled;
        DetectionModeBox.IsEnabled = enabled;
        AnimalModelBox.IsEnabled = enabled;
        ModelPathBox.IsEnabled = enabled;
    }

    private void SourceKindBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings || SourceKindBox.SelectedItem is null)
        {
            return;
        }

        _settings.SelectedSourceKind = SelectedTag(SourceKindBox);
        UpdateSourcePanels();
        SaveSettings();
    }

    private void UpdateSourcePanels()
    {
        var tag = SelectedTag(SourceKindBox);
        DevicePanel.Visibility = tag == "Device" ? Visibility.Visible : Visibility.Collapsed;
        AddressPanel.Visibility = tag == "Device" ? Visibility.Collapsed : Visibility.Visible;
        SavedStreamPanel.Visibility = tag == "Stream" ? Visibility.Visible : Visibility.Collapsed;
        BrowseVideoButton.Visibility = tag == "File" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "Stream" && string.IsNullOrWhiteSpace(AddressBox.Text))
        {
            AddressBox.Text = _settings.LastStreamAddress;
        }
    }

    private void StreamProfileBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings || StreamProfileBox.SelectedItem is not StreamChoice choice)
        {
            return;
        }

        _settings.SelectedStreamId = choice.Id;
        if (!string.IsNullOrWhiteSpace(choice.Address))
        {
            AddressBox.Text = choice.Address;
            _settings.LastStreamAddress = choice.Address;
        }

        SaveSettings();
    }

    private void AddressBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (SelectedTag(SourceKindBox) == "Stream")
        {
            _settings.LastStreamAddress = AddressBox.Text.Trim();
            SaveSettings();
        }
    }

    private void DetectionModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings || DetectionModeBox.SelectedItem is null)
        {
            return;
        }

        _settings.SelectedDetectionMode = SelectedTag(DetectionModeBox);
        UpdateModelPanels();
        SaveSettings();
    }

    private void AnimalModelBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSettings || AnimalModelBox.SelectedItem is null)
        {
            return;
        }

        _settings.AnimalModelChoice = SelectedTag(AnimalModelBox);
        UpdateModelPanels();
        SaveSettings();
    }

    private void UpdateModelPanels()
    {
        var detectionTag = SelectedTag(DetectionModeBox);
        var animalMode = detectionTag is "Animal" or "PersonAnimal";
        ModelPanel.Visibility = animalMode ? Visibility.Visible : Visibility.Collapsed;
        CustomModelPanel.Visibility = animalMode && SelectedTag(AnimalModelBox) == "Custom"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void RefreshDevicesButton_OnClick(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private void BrowseVideoButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationManager.Get("SelectVideoFile"),
            Filter = LocalizationManager.Get("VideoFileFilter")
        };
        if (dialog.ShowDialog(this) == true)
        {
            AddressBox.Text = dialog.FileName;
        }
    }

    private void BrowseModelButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationManager.Get("SelectOnnxModel"),
            Filter = LocalizationManager.Get("OnnxFileFilter")
        };
        if (dialog.ShowDialog(this) == true)
        {
            ModelPathBox.Text = dialog.FileName;
            _settings.CustomAnimalModelPath = dialog.FileName;
            SaveSettings();
        }
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings) { Owner = this };
        if (window.ShowDialog() != true)
        {
            return;
        }

        _settings = window.Result;
        AppLogger.Info($"User saved settings: language={_settings.Language}, sourceKind={_settings.SelectedSourceKind}");
        ((App)Application.Current).Settings = _settings;
        SettingsStore.Save(_settings);
        LocalizationManager.Apply(_settings.Language);
        ApplySettingsToUi();
        RefreshVersionDisplay(_pendingStoreUpdate);
    }

    private void WhitelistButton_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new WhitelistWindow(
            _whitelistRecognition,
            (name, kind) => _engine?.EnrollCurrentTarget(name, kind)
                ?? new WhitelistEnrollmentResult(WhitelistEnrollmentStatus.NoFrame),
            SelectRegionFromCurrentFrame,
            (region, name, kind) => _engine?.EnrollCurrentRegion(region, name, kind)
                ?? new WhitelistEnrollmentResult(WhitelistEnrollmentStatus.NoFrame),
            UpdateWhitelistSummary)
        {
            Owner = this
        };
        window.ShowDialog();
        UpdateWhitelistSummary();
    }

    private RegionSelectionResult? SelectRegionFromCurrentFrame()
    {
        if (_previewBitmap is null || _latestFrameWidth <= 0 || _latestFrameHeight <= 0)
        {
            MessageBox.Show(this, LocalizationManager.Get("WhitelistNoFrame"),
                LocalizationManager.Get("Information"), MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var window = new RegionSelectionWindow(_previewBitmap, _latestFrameWidth, _latestFrameHeight)
        {
            Owner = this
        };
        return window.ShowDialog() == true && window.SelectedRegion is { } region
            ? new RegionSelectionResult(region, window.SelectedName)
            : null;
    }

    private void HandleRecognitionEvents(IReadOnlyList<TrackedObject> objects)
    {
        foreach (var item in objects.Where(candidate => candidate.IsKnown.HasValue))
        {
            var kind = string.Equals(item.Label, "cat", StringComparison.OrdinalIgnoreCase)
                ? WhitelistSubjectKind.Cat
                : WhitelistSubjectKind.Face;
            var state = item.IsKnown == true
                ? $"known:{item.IdentityName}"
                : "unknown";
            if (_recognitionStates.TryGetValue(item.Id, out var previousState) && previousState == state)
            {
                continue;
            }

            _recognitionStates[item.Id] = state;
            if (IsDuplicateRecognitionEvent(item, state))
            {
                continue;
            }

            RememberRecognitionEvent(item, state);
            var typeName = kind == WhitelistSubjectKind.Face
                ? LocalizationManager.Get("WhitelistFace")
                : LocalizationManager.Get("WhitelistCat");
            var description = item.IsKnown == true
                ? LocalizationManager.Format("KnownRecognition", item.IdentityName ?? string.Empty, typeName)
                : LocalizationManager.Get(kind == WhitelistSubjectKind.Face
                    ? "UnknownFaceAlert"
                    : "UnknownCatAlert");

            var eventRecord = new RecognitionEventRecord(
                DateTimeOffset.Now,
                kind,
                item.IdentityName,
                item.IsKnown == true,
                item.Id,
                item.RecognitionDistance,
                item.RecognitionSimilarity);
            _ = Task.Run(() =>
            {
                try
                {
                    _recognitionEventStore.Append(eventRecord);
                }
                catch (IOException)
                {
                    // Recognition must keep running if the local event log is busy.
                }
            });

            if (RecognitionLogBox.Items.Count == 1 &&
                RecognitionLogBox.Items[0] is ListBoxItem placeholder &&
                !placeholder.IsHitTestVisible)
            {
                RecognitionLogBox.Items.Clear();
            }

            RecognitionLogBox.Items.Insert(
                0,
                LocalizationManager.Format("RecognitionEventFormat", DateTimeOffset.Now, description));
            while (RecognitionLogBox.Items.Count > 30)
            {
                RecognitionLogBox.Items.RemoveAt(RecognitionLogBox.Items.Count - 1);
            }

            if (item.IsKnown != true)
            {
                UnknownAlertText.Text = description;
                UnknownAlertBanner.Visibility = Visibility.Visible;
                _unknownAlertTimer.Stop();
                _unknownAlertTimer.Start();
            }
        }
    }

    private bool IsDuplicateRecognitionEvent(TrackedObject item, string state)
    {
        var now = DateTimeOffset.Now;
        _recentRecognitionEvents.RemoveAll(candidate => now - candidate.Timestamp > TimeSpan.FromSeconds(5));
        return _recentRecognitionEvents.Any(candidate =>
            string.Equals(candidate.Label, item.Label, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.State, state, StringComparison.Ordinal) &&
            IntersectionOverUnion(candidate.Box, item.Box) >= 0.15f);
    }

    private void RememberRecognitionEvent(TrackedObject item, string state) =>
        _recentRecognitionEvents.Add(new RecentRecognitionEvent(
            DateTimeOffset.Now,
            item.Label,
            state,
            item.Box));

    private static float IntersectionOverUnion(CvRect first, CvRect second)
    {
        var intersection = first & second;
        if (intersection.Width <= 0 || intersection.Height <= 0)
        {
            return 0;
        }

        var intersectionArea = intersection.Width * intersection.Height;
        var unionArea = first.Width * first.Height + second.Width * second.Height - intersectionArea;
        return unionArea <= 0 ? 0 : (float)intersectionArea / unionArea;
    }

    private void UpdateWhitelistSummary() =>
        WhitelistSummaryText.Text = LocalizationManager.Format(
            "WhitelistSummary",
            _whitelistRecognition.GetProfiles().Count);

    private sealed record RecentRecognitionEvent(DateTimeOffset Timestamp, string Label, string State, CvRect Box);

    private void ApplySettingsToUi()
    {
        _applyingSettings = true;
        try
        {
            SelectComboTag(SourceKindBox, _settings.SelectedSourceKind, "Device");
            SelectComboTag(DetectionModeBox, _settings.SelectedDetectionMode, "Face");
            SelectComboTag(AnimalModelBox, _settings.AnimalModelChoice, "BuiltIn");
            ModelPathBox.Text = _settings.CustomAnimalModelPath;
            RefreshStreamProfiles();
            UpdateSourcePanels();
            UpdateModelPanels();
            if (_engine is null)
            {
                StatusText.Text = LocalizationManager.Get("Ready");
            }

            MetricsText.Text = LocalizationManager.Format("MetricsFormat", 0d, 0);
            UpdateWhitelistSummary();
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private void RefreshStreamProfiles()
    {
        var choices = new List<StreamChoice>
        {
            new(null, LocalizationManager.Get("ManualAddress"), string.Empty)
        };
        choices.AddRange(_settings.Streams.Select(profile => new StreamChoice(profile.Id, profile.Name, profile.Address)));
        StreamProfileBox.ItemsSource = choices;
        var selected = choices.FirstOrDefault(choice => choice.Id == _settings.SelectedStreamId) ?? choices[0];
        StreamProfileBox.SelectedItem = selected;
        AddressBox.Text = string.IsNullOrWhiteSpace(selected.Address)
            ? _settings.LastStreamAddress
            : selected.Address;
    }

    private void PersistUiSelection()
    {
        _settings.SelectedSourceKind = SelectedTag(SourceKindBox);
        _settings.SelectedDetectionMode = SelectedTag(DetectionModeBox);
        _settings.AnimalModelChoice = SelectedTag(AnimalModelBox);
        _settings.CustomAnimalModelPath = ModelPathBox.Text.Trim();
        if (_settings.SelectedSourceKind == "Stream")
        {
            _settings.LastStreamAddress = AddressBox.Text.Trim();
        }

        SaveSettings();
    }

    private void SaveSettings()
    {
        ((App)Application.Current).Settings = _settings;
        SettingsStore.Save(_settings);
        AppLogger.Info("Application settings persisted");
    }

    private static string SelectedTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

    private static void SelectComboTag(ComboBox comboBox, string tag, string fallbackTag)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            ?? comboBox.Items.OfType<ComboBoxItem>().First(item => item.Tag?.ToString() == fallbackTag);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosing = true;
        PersistUiSelection();
        _engine?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _engine = null;
        _storeUpdateTimer.Stop();
        _unknownAlertTimer.Stop();
        _whitelistRecognition.Dispose();
        base.OnClosing(e);
    }

    private sealed record StreamChoice(string? Id, string Name, string Address);
}
