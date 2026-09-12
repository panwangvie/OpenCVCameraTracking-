using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using OpenCVCameraTracking.Configuration;
using OpenCVCameraTracking.Core.Camera;
using OpenCVCameraTracking.Core.Onvif;
using OpenCVCameraTracking.Core.Logging;
using System.Net;
using OpenCVCameraTracking.Localization;

namespace OpenCVCameraTracking;

public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<StreamProfile> _profiles;
    private readonly ObservableCollection<CameraDeviceProfile> _cameraProfiles;
    private readonly OnvifDiscoveryService _onvif = new();
    private readonly ObservableCollection<OnvifChoice> _onvifDevices = [];
    private readonly ObservableCollection<OnvifPresetInfo> _presets = [];

    public SettingsWindow(ApplicationSettings settings)
    {
        InitializeComponent();
        Result = settings.DeepClone();
        _profiles = new ObservableCollection<StreamProfile>(Result.Streams);
        _cameraProfiles = new ObservableCollection<CameraDeviceProfile>(Result.CameraDevices);
        ProfilesBox.ItemsSource = _profiles;
        CameraProfilesBox.ItemsSource = _cameraProfiles;
        OnvifDevicesBox.ItemsSource = _onvifDevices;
        PresetsBox.ItemsSource = _presets;
        SelectComboTag(LanguageBox, Result.Language);
        SelectComboTag(BackendBox, Result.PreferredBackend.ToString());
        SelectComboTag(LayoutBox, Result.SelectedLayout);
        LowLatencyBox.IsChecked = Result.RtspLowLatency;
        FaceConfidenceSlider.Value = Result.FaceConfidence;
        AnimalConfidenceSlider.Value = Result.AnimalConfidence;
        RefreshDefaultStreams();
        var selectedProfile = _profiles.FirstOrDefault(profile => profile.Id == Result.SelectedStreamId);
        if (selectedProfile is not null)
        {
            ProfilesBox.SelectedItem = selectedProfile;
        }
        else if (!string.IsNullOrWhiteSpace(Result.LastStreamAddress))
        {
            ProfileAddressBox.Text = Result.LastStreamAddress;
        }

        UpdateConfidenceText();
    }

    public ApplicationSettings Result { get; }

    private void ProfilesBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesBox.SelectedItem is not StreamProfile profile)
        {
            return;
        }

        ProfileNameBox.Text = profile.Name;
        ProfileAddressBox.Text = profile.Address;
        ProfileSubAddressBox.Text = profile.SubAddress;
        ProfileGroupBox.Text = profile.Group;
        ProfileNotesBox.Text = profile.Notes;
        ProfileEnabledBox.IsChecked = profile.Enabled;
    }

    private void AddOrUpdateButton_OnClick(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        var address = ProfileAddressBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
        {
            ShowInformation("ProfileRequired");
            return;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("rtsp" or "http" or "https"))
        {
            ShowInformation("InvalidStreamAddress");
            return;
        }

        if (ProfilesBox.SelectedItem is StreamProfile selected)
        {
            selected.Name = name;
            selected.Address = address;
            selected.SubAddress = ProfileSubAddressBox.Text.Trim();
            selected.Group = ProfileGroupBox.Text.Trim();
            selected.Notes = ProfileNotesBox.Text.Trim();
            selected.Enabled = ProfileEnabledBox.IsChecked == true;
            ProfilesBox.Items.Refresh();
        }
        else
        {
            var profile = new StreamProfile
            {
                Name = name,
                Address = address,
                SubAddress = ProfileSubAddressBox.Text.Trim(),
                Group = ProfileGroupBox.Text.Trim(),
                Notes = ProfileNotesBox.Text.Trim(),
                Enabled = ProfileEnabledBox.IsChecked == true
            };
            _profiles.Add(profile);
            ProfilesBox.SelectedItem = profile;
        }

        RefreshDefaultStreams();
    }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProfilesBox.SelectedItem is not StreamProfile selected)
        {
            return;
        }

        _profiles.Remove(selected);
        ProfileNameBox.Clear();
        ProfileAddressBox.Clear();
        ProfileSubAddressBox.Clear();
        ProfileGroupBox.Clear();
        ProfileNotesBox.Clear();
        ProfileEnabledBox.IsChecked = true;
        RefreshDefaultStreams();
    }

    private void RefreshDefaultStreams()
    {
        var selectedId = (DefaultStreamBox.SelectedItem as DefaultStreamChoice)?.Id ?? Result.SelectedStreamId;
        var choices = new List<DefaultStreamChoice>
        {
            new(null, LocalizationManager.Get("NoDefaultStream"))
        };
        choices.AddRange(_profiles.Select(profile => new DefaultStreamChoice(profile.Id, profile.Name)));
        DefaultStreamBox.ItemsSource = choices;
        DefaultStreamBox.SelectedItem = choices.FirstOrDefault(choice => choice.Id == selectedId) ?? choices[0];
    }

    private void ConfidenceSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        UpdateConfidenceText();

    private void UpdateConfidenceText()
    {
        if (FaceConfidenceText is null || AnimalConfidenceText is null)
        {
            return;
        }

        FaceConfidenceText.Text = $"{FaceConfidenceSlider.Value:P0}";
        AnimalConfidenceText.Text = $"{AnimalConfidenceSlider.Value:P0}";
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        Result.Language = SelectedTag(LanguageBox);
        Result.PreferredBackend = Enum.TryParse<VideoCaptureBackend>(SelectedTag(BackendBox), out var backend)
            ? backend
            : VideoCaptureBackend.Auto;
        Result.SelectedLayout = SelectedTag(LayoutBox);
        Result.RtspLowLatency = LowLatencyBox.IsChecked == true;
        Result.FaceConfidence = (float)FaceConfidenceSlider.Value;
        Result.AnimalConfidence = (float)AnimalConfidenceSlider.Value;
        Result.Streams = _profiles.ToList();
        Result.CameraDevices = _cameraProfiles.ToList();
        Result.SelectedStreamId = (DefaultStreamBox.SelectedItem as DefaultStreamChoice)?.Id;
        DialogResult = true;
    }

    private async void DiscoverOnvifButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _onvifDevices.Clear();
            var user = OnvifUserBox.Text.Trim();
            var password = OnvifPasswordBox.Password;
            var credential = string.IsNullOrWhiteSpace(user) ? null : new NetworkCredential(user, password);
            var devices = await _onvif.DiscoverAsync(TimeSpan.FromSeconds(4), credential);
            foreach (var device in devices)
            {
                foreach (var profile in device.Profiles.Where(profile => !string.IsNullOrWhiteSpace(profile.StreamUri)))
                {
                    _onvifDevices.Add(new OnvifChoice(
                        $"{device.Name ?? device.DeviceUri} · {profile.Name}",
                        profile.StreamUri!,
                        device,
                        profile));
                }
            }
            if (_onvifDevices.Count == 0)
            {
                ShowInformation("OnvifNoneFound");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLogger.Error("ONVIF discovery failed", exception);
            MessageBox.Show(this, exception.Message, LocalizationManager.Get("Information"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportOnvifButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (OnvifDevicesBox.SelectedItem is not OnvifChoice choice)
        {
            ShowInformation("OnvifSelect");
            return;
        }

        var profile = new StreamProfile
        {
            Name = choice.DisplayName,
            Address = choice.Address,
            Group = "ONVIF",
            Notes = choice.Device.SupportsProfileT ? "Profile T" : "Profile S / legacy"
        };
        _profiles.Add(profile);
        ProfilesBox.SelectedItem = profile;
        RefreshDefaultStreams();
    }

    private void CameraProfilesBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CameraProfilesBox.SelectedItem is not CameraDeviceProfile profile)
        {
            return;
        }

        CameraProfileGroupBox.Text = profile.Group;
        CameraProfileNotesBox.Text = profile.Notes;
        CameraProfileEnabledBox.IsChecked = profile.Enabled;
    }

    private void SaveCameraProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (CameraProfilesBox.SelectedItem is not CameraDeviceProfile profile)
        {
            return;
        }

        profile.Group = CameraProfileGroupBox.Text.Trim();
        profile.Notes = CameraProfileNotesBox.Text.Trim();
        profile.Enabled = CameraProfileEnabledBox.IsChecked == true;
        CameraProfilesBox.Items.Refresh();
    }

    private async void PtzLeftButton_OnClick(object sender, RoutedEventArgs e) => await SendPtzAsync(-0.35f, 0, 0);
    private async void PtzRightButton_OnClick(object sender, RoutedEventArgs e) => await SendPtzAsync(0.35f, 0, 0);
    private async void PtzVerticalButton_OnClick(object sender, RoutedEventArgs e) => await SendPtzAsync(0, 0.35f, 0);
    private async void PtzStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (OnvifDevicesBox.SelectedItem is not OnvifChoice choice) return;
        try
        {
            await _onvif.StopAsync(choice.Device.DeviceUri, choice.Profile.Token, CreateOnvifCredential());
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"ONVIF PTZ stop failed: {exception.Message}");
        }
    }

    private async Task SendPtzAsync(float pan, float tilt, float zoom)
    {
        if (OnvifDevicesBox.SelectedItem is not OnvifChoice choice || !choice.Device.SupportsPtz)
        {
            ShowInformation("OnvifPtzUnavailable");
            return;
        }

        try
        {
            await _onvif.ContinuousMoveAsync(
                choice.Device.DeviceUri,
                choice.Profile.Token,
                pan,
                tilt,
                zoom,
                TimeSpan.FromMilliseconds(600),
                CreateOnvifCredential());
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"ONVIF PTZ command failed: {exception.Message}");
            MessageBox.Show(this, exception.Message, LocalizationManager.Get("Information"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private NetworkCredential? CreateOnvifCredential()
    {
        var user = OnvifUserBox.Text.Trim();
        return string.IsNullOrWhiteSpace(user) ? null : new NetworkCredential(user, OnvifPasswordBox.Password);
    }

    private async void OnvifDevicesBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => await RefreshPresetsAsync();

    private async void RefreshPresetsButton_OnClick(object sender, RoutedEventArgs e) => await RefreshPresetsAsync();

    private async Task RefreshPresetsAsync()
    {
        _presets.Clear();
        if (OnvifDevicesBox.SelectedItem is not OnvifChoice choice || !choice.Device.SupportsPtz)
        {
            return;
        }

        try
        {
            var presets = await _onvif.GetPresetsAsync(
                choice.Device.DeviceUri,
                choice.Profile.Token,
                CreateOnvifCredential());
            foreach (var preset in presets) _presets.Add(preset);
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"ONVIF preset query failed: {exception.Message}");
        }
    }

    private async void GotoPresetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (OnvifDevicesBox.SelectedItem is not OnvifChoice choice || PresetsBox.SelectedItem is not OnvifPresetInfo preset)
        {
            return;
        }

        try
        {
            await _onvif.GotoPresetAsync(choice.Device.DeviceUri, choice.Profile.Token, preset.Token, CreateOnvifCredential());
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"ONVIF goto preset failed: {exception.Message}");
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowInformation(string resourceKey) =>
        MessageBox.Show(
            this,
            LocalizationManager.Get(resourceKey),
            LocalizationManager.Get("Information"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private static string SelectedTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "zh-CN";

    private static void SelectComboTag(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
        comboBox.SelectedIndex = comboBox.SelectedIndex < 0 ? 0 : comboBox.SelectedIndex;
    }

    private sealed record DefaultStreamChoice(string? Id, string Name)
    {
        public override string ToString() => Name;
    }
    private sealed record OnvifChoice(string DisplayName, string Address, OnvifDeviceInfo Device, OnvifProfileInfo Profile);
}
