using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace OpenCVCameraTracking;

public partial class MultiCameraSourceSelectionWindow : Window
{
    private readonly ObservableCollection<SourceSelectionItem> _items = [];

    public MultiCameraSourceSelectionWindow(
        IEnumerable<MultiPreviewSource> sources,
        IEnumerable<string> selectedKeys)
    {
        InitializeComponent();
        var selected = new HashSet<string>(selectedKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            _items.Add(new SourceSelectionItem(source, selected.Contains(source.Key)));
        }
        SourceList.ItemsSource = _items;
    }

    public IReadOnlyList<MultiPreviewSource> SelectedSources => _items
        .Where(item => item.IsSelected)
        .Select(item => item.Source)
        .ToArray();

    private void OpenPreviewButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SelectedSources.Count == 0)
        {
            MessageBox.Show(this, Localization.LocalizationManager.Get("MultiCameraNoSelection"),
                Localization.LocalizationManager.Get("Information"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class SourceSelectionItem(MultiPreviewSource source, bool isSelected) : INotifyPropertyChanged
    {
        private bool _isSelected = isSelected;
        public MultiPreviewSource Source { get; } = source;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
