using System.Windows;
using System.Windows.Controls;
using OpenCVCameraTracking.Core.Recognition;
using OpenCVCameraTracking.Localization;

namespace OpenCVCameraTracking;

public partial class WhitelistWindow : Window
{
    private readonly WhitelistRecognitionService _service;
    private readonly Func<string, WhitelistSubjectKind, WhitelistEnrollmentResult> _enrollCurrent;

    public WhitelistWindow(
        WhitelistRecognitionService service,
        Func<string, WhitelistSubjectKind, WhitelistEnrollmentResult> enrollCurrent)
    {
        InitializeComponent();
        _service = service;
        _enrollCurrent = enrollCurrent;
        StoragePathText.Text = service.StorageDirectory;
        RefreshProfiles();
    }

    private void EnrollButton_OnClick(object sender, RoutedEventArgs e)
    {
        var kind = (KindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Cat"
            ? WhitelistSubjectKind.Cat
            : WhitelistSubjectKind.Face;
        var result = _enrollCurrent(NameBox.Text, kind);
        if (result.Status == WhitelistEnrollmentStatus.Success)
        {
            NameBox.Clear();
            RefreshProfiles();
            ShowInformation(LocalizationManager.Format(
                "WhitelistEnrollSuccess",
                result.Profile!.Name,
                result.Profile.SampleCount));
            return;
        }

        var resourceKey = result.Status switch
        {
            WhitelistEnrollmentStatus.InvalidName => "WhitelistNameRequired",
            WhitelistEnrollmentStatus.NoFrame => "WhitelistNoFrame",
            WhitelistEnrollmentStatus.NoMatchingTarget => kind == WhitelistSubjectKind.Face
                ? "WhitelistNoFace"
                : "WhitelistNoCat",
            _ => "WhitelistUnavailable"
        };
        ShowInformation(LocalizationManager.Get(resourceKey));
    }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ProfileItem selected)
        {
            ShowInformation(LocalizationManager.Get("WhitelistSelectDelete"));
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            LocalizationManager.Format("WhitelistDeleteConfirm", selected.Name),
            LocalizationManager.Get("WhitelistTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _service.Delete(selected.Id);
        RefreshProfiles();
    }

    private void RefreshProfiles()
    {
        ProfileList.ItemsSource = _service.GetProfiles()
            .Select(profile => new ProfileItem(
                profile.Id,
                profile.Name,
                profile.Kind == WhitelistSubjectKind.Face
                    ? LocalizationManager.Get("WhitelistFace")
                    : LocalizationManager.Get("WhitelistCat"),
                LocalizationManager.Format("WhitelistSampleCount", profile.SampleCount)))
            .ToArray();
    }

    private void ShowInformation(string message) => MessageBox.Show(
        this,
        message,
        LocalizationManager.Get("Information"),
        MessageBoxButton.OK,
        MessageBoxImage.Information);

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private sealed record ProfileItem(
        string Id,
        string Name,
        string TypeText,
        string SampleText);
}
