using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using OpenCVCameraTracking.Localization;
using CvRect = OpenCvSharp.Rect;

namespace OpenCVCameraTracking;

public partial class RegionSelectionWindow : Window
{
    private readonly int _frameWidth;
    private readonly int _frameHeight;
    private Point _start;
    private bool _hasSelection;

    public CvRect? SelectedRegion { get; private set; }
    public string SelectedName { get; private set; } = string.Empty;

    public RegionSelectionWindow(BitmapSource frame, int frameWidth, int frameHeight)
    {
        InitializeComponent();
        PreviewImage.Source = frame;
        _frameWidth = frameWidth;
        _frameHeight = frameHeight;
    }

    private void SelectionCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(SelectionCanvas);
        SelectionCanvas.CaptureMouse();
        UpdateRectangle(_start, _start);
        SelectionRectangle.Visibility = Visibility.Visible;
        _hasSelection = false;
    }

    private void SelectionCanvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (SelectionCanvas.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateRectangle(_start, e.GetPosition(SelectionCanvas));
        }
    }

    private void SelectionCanvas_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!SelectionCanvas.IsMouseCaptured)
        {
            return;
        }

        var end = e.GetPosition(SelectionCanvas);
        SelectionCanvas.ReleaseMouseCapture();
        var rect = Normalize(_start, end);
        _hasSelection = rect.Width >= 24 && rect.Height >= 24;
        if (_hasSelection)
        {
            SelectedRegion = ToFrameRect(rect);
        }
        UpdateConfirmState();
        e.Handled = true;
    }

    private void NameBox_OnTextChanged(object sender, TextChangedEventArgs e) => UpdateConfirmState();

    private void UpdateConfirmState() => ConfirmButton.IsEnabled =
        _hasSelection && !string.IsNullOrWhiteSpace(NameBox.Text);

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_hasSelection || SelectedRegion is null || string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, !_hasSelection ? LocalizationManager.Get("SelectionTooSmall") : LocalizationManager.Get("WhitelistNameRequired"), LocalizationManager.Get("Information"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedName = NameBox.Text.Trim();
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void UpdateRectangle(Point first, Point second)
    {
        var x = Math.Min(first.X, second.X);
        var y = Math.Min(first.Y, second.Y);
        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = Math.Max(1, Math.Abs(first.X - second.X));
        SelectionRectangle.Height = Math.Max(1, Math.Abs(first.Y - second.Y));
    }

    private CvRect ToFrameRect(Rect view)
    {
        var scale = Math.Min(SelectionCanvas.ActualWidth / _frameWidth, SelectionCanvas.ActualHeight / _frameHeight);
        var imageWidth = _frameWidth * scale;
        var imageHeight = _frameHeight * scale;
        var offsetX = (SelectionCanvas.ActualWidth - imageWidth) / 2;
        var offsetY = (SelectionCanvas.ActualHeight - imageHeight) / 2;
        var left = Math.Clamp((view.Left - offsetX) / scale, 0, _frameWidth - 1);
        var top = Math.Clamp((view.Top - offsetY) / scale, 0, _frameHeight - 1);
        var right = Math.Clamp((view.Right - offsetX) / scale, left + 1, _frameWidth);
        var bottom = Math.Clamp((view.Bottom - offsetY) / scale, top + 1, _frameHeight);
        return new CvRect((int)Math.Round(left), (int)Math.Round(top),
            Math.Max(1, (int)Math.Round(right - left)), Math.Max(1, (int)Math.Round(bottom - top)));
    }

    private static Rect Normalize(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

public sealed record RegionSelectionResult(CvRect Region, string Name);
