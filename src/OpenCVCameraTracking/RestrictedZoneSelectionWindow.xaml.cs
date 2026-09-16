using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using OpenCVCameraTracking.Localization;
using CvRect = OpenCvSharp.Rect;

namespace OpenCVCameraTracking;

public partial class RestrictedZoneSelectionWindow : Window
{
    private readonly int _frameWidth;
    private readonly int _frameHeight;
    private Point _start;
    private bool _hasSelection;

    public CvRect? SelectedRegion { get; private set; }

    public RestrictedZoneSelectionWindow(BitmapSource frame, int frameWidth, int frameHeight)
    {
        InitializeComponent();
        PreviewImage.Source = frame;
        _frameWidth = frameWidth;
        _frameHeight = frameHeight;
    }

    private void SelectionCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var imageBounds = GetImageBounds();
        var position = e.GetPosition(SelectionCanvas);
        if (!imageBounds.Contains(position))
        {
            return;
        }

        _start = ClampToImage(position, imageBounds);
        SelectionCanvas.CaptureMouse();
        UpdateRectangle(_start, _start);
        SelectionRectangle.Visibility = Visibility.Visible;
        _hasSelection = false;
    }

    private void SelectionCanvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (SelectionCanvas.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateRectangle(_start, ClampToImage(e.GetPosition(SelectionCanvas), GetImageBounds()));
        }
    }

    private void SelectionCanvas_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!SelectionCanvas.IsMouseCaptured)
        {
            return;
        }

        var end = ClampToImage(e.GetPosition(SelectionCanvas), GetImageBounds());
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

    private void UpdateConfirmState() => ConfirmButton.IsEnabled = _hasSelection && SelectedRegion is not null;

    private void ConfirmButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_hasSelection || SelectedRegion is null)
        {
            MessageBox.Show(this, LocalizationManager.Get("SelectionTooSmall"), LocalizationManager.Get("Information"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

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
        var imageBounds = GetImageBounds();
        var scale = imageBounds.Width / _frameWidth;
        var left = Math.Clamp((view.Left - imageBounds.Left) / scale, 0, _frameWidth - 1);
        var top = Math.Clamp((view.Top - imageBounds.Top) / scale, 0, _frameHeight - 1);
        var right = Math.Clamp((view.Right - imageBounds.Left) / scale, left + 1, _frameWidth);
        var bottom = Math.Clamp((view.Bottom - imageBounds.Top) / scale, top + 1, _frameHeight);
        return new CvRect((int)Math.Round(left), (int)Math.Round(top),
            Math.Max(1, (int)Math.Round(right - left)), Math.Max(1, (int)Math.Round(bottom - top)));
    }

    private Rect GetImageBounds()
    {
        if (_frameWidth <= 0 || _frameHeight <= 0 ||
            SelectionCanvas.ActualWidth <= 0 || SelectionCanvas.ActualHeight <= 0)
        {
            return new Rect(0, 0, SelectionCanvas.ActualWidth, SelectionCanvas.ActualHeight);
        }

        var scale = Math.Min(SelectionCanvas.ActualWidth / _frameWidth, SelectionCanvas.ActualHeight / _frameHeight);
        var width = _frameWidth * scale;
        var height = _frameHeight * scale;
        return new Rect(
            (SelectionCanvas.ActualWidth - width) / 2,
            (SelectionCanvas.ActualHeight - height) / 2,
            width,
            height);
    }

    private static Point ClampToImage(Point position, Rect bounds) => new(
        Math.Clamp(position.X, bounds.Left, bounds.Right),
        Math.Clamp(position.Y, bounds.Top, bounds.Bottom));

    private static Rect Normalize(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
