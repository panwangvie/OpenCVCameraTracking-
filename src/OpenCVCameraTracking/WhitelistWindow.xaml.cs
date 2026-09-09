using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CvRect = OpenCvSharp.Rect;
using OpenCVCameraTracking.Core.Recognition;
using OpenCVCameraTracking.Localization;

namespace OpenCVCameraTracking;

public partial class WhitelistWindow : Window
{
    private readonly WhitelistRecognitionService _service;
    private readonly Func<string, WhitelistSubjectKind, WhitelistEnrollmentResult> _enrollCurrent;
    private readonly Func<RegionSelectionResult?>? _selectRegion;
    private readonly Func<CvRect, string, WhitelistSubjectKind, WhitelistEnrollmentResult>? _enrollRegion;

    public WhitelistWindow(
        WhitelistRecognitionService service,
        Func<string, WhitelistSubjectKind, WhitelistEnrollmentResult> enrollCurrent,
        Func<RegionSelectionResult?>? selectRegion = null,
        Func<CvRect, string, WhitelistSubjectKind, WhitelistEnrollmentResult>? enrollRegion = null)
    {
        InitializeComponent();
        _service = service;
        _enrollCurrent = enrollCurrent;
        _selectRegion = selectRegion;
        _enrollRegion = enrollRegion;
        StoragePathText.Text = service.StorageDirectory;
        RefreshProfiles();
    }

    private void SelectRegionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectRegion is null || _enrollRegion is null)
        {
            ShowInformation(LocalizationManager.Get("WhitelistNoFrame"));
            return;
        }

        var region = _selectRegion();
        if (region is null)
        {
            return;
        }

        var kind = (KindBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Cat"
            ? WhitelistSubjectKind.Cat
            : WhitelistSubjectKind.Face;
        NameBox.Text = region.Name;
        var result = _enrollRegion(region.Region, region.Name, kind);
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
            _ => "WhitelistUnavailable"
        };
        ShowInformation(LocalizationManager.Get(resourceKey));
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
                LocalizationManager.Format("WhitelistSampleCount", profile.SampleCount),
                _service.GetSamplePaths(profile.Id).Select(path => new SampleItem(path)).ToArray()))
            .ToArray();
    }

    private void Sample_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || sender is not Image { DataContext: SampleItem sample })
        {
            return;
        }

        var image = new Image
        {
            Source = new BitmapImage(new Uri(sample.Path)),
            Stretch = Stretch.Uniform,
            Margin = new Thickness(12)
        };
        var viewer = new Window
        {
            Title = LocalizationManager.Get("SamplePreview"),
            Owner = this,
            Width = 420,
            Height = 420,
            Background = new SolidColorBrush(Color.FromRgb(16, 20, 24)),
            Content = image,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        viewer.ShowDialog();
        e.Handled = true;
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
        string SampleText,
        IReadOnlyList<SampleItem> Samples);

    private sealed record SampleItem(string Path);
}
