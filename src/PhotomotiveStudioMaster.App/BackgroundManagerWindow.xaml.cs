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

    public BackgroundManagerWindow()
    {
        InitializeComponent();
        RefreshCategories();
        RefreshLibrary();
    }

    private void FilterChanged(object sender, EventArgs e) => RefreshLibrary();

    private void RefreshCategories()
    {
        var current = CategoryFilter.SelectedItem?.ToString() ?? "All";
        CategoryFilter.ItemsSource = _library.GetCategories();
        CategoryFilter.SelectedItem = CategoryFilter.Items.Cast<string>()
            .FirstOrDefault(x => x.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? "All";
    }

    private void RefreshLibrary()
    {
        if (!IsLoaded && CategoryFilter.ItemsSource is null)
            return;

        var category = CategoryFilter.SelectedItem?.ToString() ?? "All";
        var items = _library.Filter(SearchBox.Text, category);
        BackgroundList.ItemsSource = items;
        VisibleCountText.Text = $"{items.Count} shown";

        var stats = _library.GetStatistics();
        StatsText.Text = $"{stats.Count} backgrounds  •  {stats.Favorites} favorites  •  {FormatBytes(stats.TotalBytes)}";
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
        var category = CategoryFilter.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(category) || category is "All" or "Favorites" or "Recent")
            category = "Custom";

        StatusText.Text = "Importing backgrounds and generating thumbnails...";
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

        NoSelectionText.Visibility = Visibility.Collapsed;
        MetadataPanel.Visibility = Visibility.Visible;
        ActionPanel.Visibility = Visibility.Visible;
        NameBox.Text = _selected.Name;
        CategoryBox.Text = _selected.Category;
        TagsBox.Text = _selected.Tags;
        AssetInfoText.Text = $"{_selected.StorageDisplay}  •  Added {_selected.CreatedAt:g}";
        FavoriteButton.Content = _selected.IsFavorite ? "REMOVE FAVORITE" : "ADD FAVORITE";
        LoadPreview(_selected.FilePath);
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
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;

        _library.ToggleFavorite(_selected);
        FavoriteButton.Content = _selected.IsFavorite ? "REMOVE FAVORITE" : "ADD FAVORITE";
        StatusText.Text = _selected.IsFavorite
            ? $"{_selected.Name} added to Favorites."
            : $"{_selected.Name} removed from Favorites.";
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
