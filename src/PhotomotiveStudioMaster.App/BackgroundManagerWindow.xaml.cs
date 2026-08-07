using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PhotomotiveStudioMaster.App.Models;
using PhotomotiveStudioMaster.App.Services;

namespace PhotomotiveStudioMaster.App;

public partial class BackgroundManagerWindow : Window
{
    private readonly BackgroundLibraryService _library = new();
    private BackgroundRecord? _selected;
    private bool _loadingSelection;

    public BackgroundManagerWindow()
    {
        InitializeComponent();
        RefreshCategories();
        RefreshLibrary();
    }

    private void FilterChanged(object sender, EventArgs e) => RefreshLibrary();

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            RefreshLibrary();
    }

    private void RefreshCategories()
    {
        var current = CategoryList.SelectedItem?.ToString() ?? "All";
        CategoryList.ItemsSource = _library.GetCategories();
        CategoryList.SelectedItem = CategoryList.Items.Cast<string>()
            .FirstOrDefault(x => x.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? "All";
    }

    private void RefreshLibrary()
    {
        var category = CategoryList.SelectedItem?.ToString() ?? "All";
        var items = _library.Filter(SearchBox.Text, category);
        BackgroundList.ItemsSource = items;
        VisibleCountText.Text = $"{items.Count:N0} shown";

        var stats = _library.GetStatistics();
        StatsText.Text = $"{stats.Count:N0} backgrounds  •  {stats.Favorites:N0} favorites  •  {FormatBytes(stats.TotalBytes)}";
    }

    private async void ImportBackgrounds_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Backgrounds",
            Filter = "Image backgrounds|*.jpg;*.jpeg;*.png;*.tif;*.tiff|All files|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true)
            await ImportFilesAsync(dialog.FileNames);
    }

    private async Task ImportFilesAsync(IEnumerable<string> files)
    {
        var category = CategoryList.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(category) || category is "All" or "Favorites" or "Recent")
            category = "Custom";

        StatusText.Text = "Importing backgrounds and generating premium thumbnails...";
        IsEnabled = false;
        try
        {
            var result = await _library.ImportAsync(files, category);
            RefreshCategories();
            RefreshLibrary();
            StatusText.Text = $"Import complete: {result.Imported} imported, {result.Skipped} skipped, {result.Errors.Count} errors.";

            if (result.Errors.Count > 0)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, result.Errors.Take(10)),
                    "Background Import",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void BackgroundList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = BackgroundList.SelectedItem as BackgroundRecord;
        if (_selected is null)
        {
            ClearSelection();
            return;
        }

        if (_selected.PixelWidth <= 0 || _selected.PixelHeight <= 0)
            _library.RefreshImageMetadata(_selected);

        _loadingSelection = true;
        try
        {
            NoSelectionText.Visibility = Visibility.Collapsed;
            MetadataPanel.Visibility = Visibility.Visible;
            ActionPanel.Visibility = Visibility.Visible;
            NameBox.Text = _selected.Name;
            CategoryBox.Text = _selected.Category;
            TagsBox.Text = _selected.Tags;
            SelectedNameText.Text = _selected.Name;
            SelectedSummaryText.Text = $"{_selected.Category}  •  {_selected.FavoriteGlyph} Favorite  •  {_selected.RatingDisplay}";
            ResolutionText.Text = _selected.ResolutionDisplay;
            AssetInfoText.Text = $"{_selected.StorageDisplay}  •  Added {_selected.CreatedAt:g}" +
                                 (_selected.LastUsedAt is null ? string.Empty : $"  •  Last used {_selected.LastUsedAt:g}");
            UseCountText.Text = _selected.UseCount.ToString("N0");
            FavoriteButton.Content = _selected.IsFavorite ? "REMOVE FAVORITE" : "ADD FAVORITE";
            RatingBox.SelectedIndex = Math.Clamp(_selected.Rating, 0, 5);
            LoadPreview(_selected.FilePath);
        }
        finally
        {
            _loadingSelection = false;
        }
    }

    private void SaveDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;

        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(CategoryBox.Text))
        {
            MessageBox.Show("Name and category are required.", "Background Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _selected.Name = NameBox.Text.Trim();
        _selected.Category = CategoryBox.Text.Trim();
        _selected.Tags = TagsBox.Text.Trim();
        _library.SaveMetadata(_selected);
        StatusText.Text = $"Saved metadata for {_selected.Name}.";
        RefreshCategories();
        RefreshLibrary();
        BackgroundList.SelectedItem = _selected;
    }

    private void RatingBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSelection || _selected is null || RatingBox.SelectedItem is not ComboBoxItem item)
            return;

        if (int.TryParse(item.Tag?.ToString(), out var rating))
        {
            _selected.Rating = Math.Clamp(rating, 0, 5);
            _library.SaveMetadata(_selected);
            SelectedSummaryText.Text = $"{_selected.Category}  •  {_selected.FavoriteGlyph} Favorite  •  {_selected.RatingDisplay}";
            StatusText.Text = rating == 0 ? $"Cleared rating for {_selected.Name}." : $"Rated {_selected.Name} {rating} of 5 stars.";
            RefreshLibrary();
        }
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;

        _library.ToggleFavorite(_selected);
        FavoriteButton.Content = _selected.IsFavorite ? "REMOVE FAVORITE" : "ADD FAVORITE";
        SelectedSummaryText.Text = $"{_selected.Category}  •  {_selected.FavoriteGlyph} Favorite  •  {_selected.RatingDisplay}";
        StatusText.Text = _selected.IsFavorite
            ? $"{_selected.Name} added to Favorites."
            : $"{_selected.Name} removed from Favorites.";
        RefreshLibrary();
    }

    private void OpenBackground_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || !File.Exists(_selected.FilePath))
            return;

        _library.MarkUsed(_selected);
        UseCountText.Text = _selected.UseCount.ToString("N0");
        AssetInfoText.Text = $"{_selected.StorageDisplay}  •  Added {_selected.CreatedAt:g}  •  Last used {_selected.LastUsedAt:g}";
        Process.Start(new ProcessStartInfo
        {
            FileName = _selected.FilePath,
            UseShellExecute = true
        });
        StatusText.Text = $"Opened {_selected.Name}.";
        RefreshLibrary();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;

        var answer = MessageBox.Show(
            $"Delete '{_selected.Name}' from the managed background library?\n\nThis removes the library copy and thumbnail. Existing source files outside Studio Master are not touched.",
            "Delete Background",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        var name = _selected.Name;
        PreviewImage.Source = null;
        _library.Delete(_selected);
        _selected = null;
        ClearSelection();
        RefreshCategories();
        RefreshLibrary();
        StatusText.Text = $"Deleted {name} from the background library.";
    }

    private void OpenLibraryFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_library.LibraryRoot);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{_library.LibraryRoot}\"",
            UseShellExecute = true
        });
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            await ImportFilesAsync(files);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void LoadPreview(string path)
    {
        PreviewImage.Source = null;
        if (!File.Exists(path))
            return;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Preview error: {ex.Message}";
        }
    }

    private void ClearSelection()
    {
        PreviewImage.Source = null;
        MetadataPanel.Visibility = Visibility.Collapsed;
        ActionPanel.Visibility = Visibility.Collapsed;
        NoSelectionText.Visibility = Visibility.Visible;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / 1024.0 / 1024.0:0.0} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:0.00} GB";
    }
}
