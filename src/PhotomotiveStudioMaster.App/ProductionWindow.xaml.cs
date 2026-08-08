using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PhotomotiveStudioMaster.App.Models;
using PhotomotiveStudioMaster.App.Services;

namespace PhotomotiveStudioMaster.App;

public partial class ProductionWindow : Window
{
    private readonly EventRecord _activeEvent;
    private readonly ImportService _importService = new();
    private readonly VehicleExtractionService _extractionService = new();
    private readonly ObservableCollection<ImportCandidate> _candidates = new();
    private readonly ObservableCollection<ImportRecord> _importedJobs = new();

    public ProductionWindow(EventRecord activeEvent)
    {
        InitializeComponent();
        _activeEvent = activeEvent;
        EventText.Text = $"{activeEvent.EventCode} • {activeEvent.Name}";
        CandidateGrid.ItemsSource = _candidates;
        ImportedGrid.ItemsSource = _importedJobs;

        RefreshDrives();
        RefreshImportedJobs();
        RefreshAiStatus();
        UpdateSelectedJobState();
    }

    private void RefreshDrives_Click(object sender, RoutedEventArgs e) => RefreshDrives();

    private void AiRuntime_Click(object sender, RoutedEventArgs e)
    {
        var window = new AiRuntimeManagerWindow { Owner = this };
        window.ShowDialog();
        RefreshAiStatus();
        UpdateSelectedJobState();
    }

    private void ImportedGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => UpdateSelectedJobState();

    private async void AddLocalPhotos_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add Photos to Active Event",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Supported photos (*.jpg;*.jpeg;*.png;*.tif;*.tiff)|*.jpg;*.jpeg;*.png;*.tif;*.tiff|JPEG photos (*.jpg;*.jpeg)|*.jpg;*.jpeg|PNG images (*.png)|*.png|TIFF images (*.tif;*.tiff)|*.tif;*.tiff|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true || dialog.FileNames.Length == 0)
            return;

        _candidates.Clear();
        foreach (var path in dialog.FileNames)
        {
            try
            {
                var info = new FileInfo(path);
                _candidates.Add(new ImportCandidate
                {
                    SourcePath = path,
                    SizeBytes = info.Length
                });
            }
            catch
            {
                // Unavailable files are ignored; the import result reports any later failures.
            }
        }

        CandidateCountText.Text = $"{_candidates.Count} local file{(_candidates.Count == 1 ? string.Empty : "s")}";
        ScanSummaryText.Text = _candidates.Count == 0
            ? "No usable local photos selected"
            : $"{_candidates.Count} local photo{(_candidates.Count == 1 ? string.Empty : "s")} selected";

        if (_candidates.Count == 0)
            return;

        AddLocalPhotosButton.IsEnabled = false;
        ImportButton.IsEnabled = false;
        ImportProgressBar.IsIndeterminate = false;
        ImportProgressBar.Value = 0;
        SetProgressState("Importing local photos…", "AccentBrush");
        StatusText.Text = "Adding local photos to the active event...";
        DetailText.Text = "Files are being copied and checksum verified before entering the Production Queue.";

        var progress = new Progress<ImportProgress>(p =>
        {
            ImportProgressBar.Value = p.Total == 0 ? 0 : p.Current * 100.0 / p.Total;
            StatusText.Text = $"{p.Status}: {p.FileName}";
            DetailText.Text = $"Local photo {p.Current} of {p.Total}";
            ExtractionProgressText.Text = $"Importing {p.Current} of {p.Total}";
        });

        try
        {
            var result = await _importService.ImportAsync(_activeEvent, _candidates.ToList(), progress);
            RefreshImportedJobs();
            ImportProgressBar.Value = 100;
            StatusText.Text = $"Local import complete: {result.Imported} imported, {result.Duplicates} duplicates skipped, {result.Errors} errors.";
            DetailText.Text = result.Imported > 0
                ? "The new jobs are ready in the Production Queue. Select one and click Extract Selected."
                : "No new jobs were added.";
            SetProgressState(result.Errors == 0 ? "✓ Local import complete" : "Local import completed with errors",
                result.Errors == 0 ? "SuccessBrush" : "WarningBrush");

            if (result.Errors > 0)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, result.ErrorMessages.Take(10)),
                    "Local Import Completed with Errors",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Local photo import failed.";
            DetailText.Text = ex.Message;
            SetProgressState("Local import failed", "ErrorBrush");
            MessageBox.Show(ex.Message, "Local Photo Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AddLocalPhotosButton.IsEnabled = true;
            ImportButton.IsEnabled = false;
        }
    }

    private void OpenComposer_Click(object sender, RoutedEventArgs e)
    {
        if (ImportedGrid.SelectedItem is not ImportRecord selected)
        {
            MessageBox.Show("Select an event job first.", "Automotive Photo Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(selected.ExtractionPath) || !File.Exists(selected.ExtractionPath))
        {
            MessageBox.Show(
                "This job does not have an extracted vehicle yet. Run Extract Selected first.",
                "Automotive Photo Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var selectedId = selected.Id;
        var window = new ComposerWindow(_activeEvent, selected) { Owner = this };
        window.ShowDialog();
        RefreshImportedJobs(selectedId);
    }

    private void RefreshDrives()
    {
        var drives = _importService.GetRemovableDrives()
            .Select(d => new DriveChoice(d.Name, $"{d.Name}  {SafeVolumeLabel(d)}"))
            .ToList();

        DriveCombo.ItemsSource = drives;
        if (drives.Count > 0)
        {
            DriveCombo.SelectedIndex = 0;
            StatusText.Text = $"{drives.Count} removable drive{(drives.Count == 1 ? string.Empty : "s")} detected.";
        }
        else
        {
            StatusText.Text = "No removable drives detected. Scan an SD card or use Add Local Photos.";
        }
    }

    private void RefreshAiStatus()
    {
        var status = _extractionService.GetRuntimeStatus();
        AiStatusText.Text = status.IsReady ? "● Local AI Ready" : "○ AI Setup Required";
        AiStatusText.ToolTip = status.Message;
        ExtractionDetailText.Text = status.IsReady
            ? "Select an imported job, then click Extract Selected."
            : "Select a job and click Extract Selected; AI Runtime setup will open automatically.";
    }

    private void ScanCard_Click(object sender, RoutedEventArgs e)
    {
        if (DriveCombo.SelectedItem is not DriveChoice drive)
        {
            MessageBox.Show("Insert or select an SD card first.", "SD Card Import", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusText.Text = "Scanning card for supported image files...";
        _candidates.Clear();

        try
        {
            var found = _importService.ScanDrive(drive.RootPath);
            foreach (var item in found)
                _candidates.Add(item);

            CandidateCountText.Text = $"{_candidates.Count} files";
            ScanSummaryText.Text = _candidates.Count == 0
                ? "No supported image files found"
                : $"{_candidates.Count} image files found";
            ImportButton.IsEnabled = _candidates.Count > 0;
            StatusText.Text = _candidates.Count > 0
                ? "Card scan complete. Review the list, then click Import All."
                : "Card scan complete. No supported images were found.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Card scan failed.";
            MessageBox.Show(ex.Message, "SD Card Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportAll_Click(object sender, RoutedEventArgs e)
    {
        if (_candidates.Count == 0)
            return;

        ImportButton.IsEnabled = false;
        AddLocalPhotosButton.IsEnabled = false;
        DriveCombo.IsEnabled = false;
        ImportProgressBar.IsIndeterminate = false;
        ImportProgressBar.Value = 0;
        SetProgressState("Importing photos…", "AccentBrush");

        var progress = new Progress<ImportProgress>(p =>
        {
            ImportProgressBar.Value = p.Total == 0 ? 0 : p.Current * 100.0 / p.Total;
            StatusText.Text = $"{p.Status}: {p.FileName}";
            DetailText.Text = $"File {p.Current} of {p.Total}";
            ExtractionProgressText.Text = $"Importing {p.Current} of {p.Total}";
        });

        try
        {
            var result = await _importService.ImportAsync(_activeEvent, _candidates.ToList(), progress);
            RefreshImportedJobs();
            ImportProgressBar.Value = 100;
            StatusText.Text = $"Import complete: {result.Imported} imported, {result.Duplicates} duplicates skipped, {result.Errors} errors.";
            DetailText.Text = "Copied files were SHA-256 verified before being accepted into the event.";
            SetProgressState(result.Errors == 0 ? "✓ Import complete" : "Import completed with errors", result.Errors == 0 ? "SuccessBrush" : "WarningBrush");

            if (result.Errors > 0)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, result.ErrorMessages.Take(10)),
                    "Import Completed with Errors",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Import stopped because of an unexpected error.";
            SetProgressState("Import failed", "ErrorBrush");
            MessageBox.Show(ex.Message, "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImportButton.IsEnabled = _candidates.Count > 0;
            AddLocalPhotosButton.IsEnabled = true;
            DriveCombo.IsEnabled = true;
        }
    }

    private async void ExtractSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ImportedGrid.SelectedItem is not ImportRecord selected)
        {
            MessageBox.Show("Select an imported job first.", "Vehicle Extraction", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var runtime = _extractionService.GetRuntimeStatus();
        if (!runtime.IsReady)
        {
            var setup = new AiRuntimeManagerWindow { Owner = this };
            setup.ShowDialog();
            RefreshAiStatus();
            runtime = _extractionService.GetRuntimeStatus();
            if (!runtime.IsReady)
            {
                UpdateSelectedJobState();
                return;
            }
        }

        var selectedId = selected.Id;
        ExtractButton.IsEnabled = false;
        PhotoStudioButton.IsEnabled = false;
        AddLocalPhotosButton.IsEnabled = false;
        StatusText.Text = $"Extracting vehicle from {selected.JobNumber}...";
        DetailText.Text = "Local AI processing is running. The first extraction may take longer while the model initializes.";
        PreviewStatusText.Text = "Extracting…";
        ExtractedPreviewImage.Source = null;
        ImportProgressBar.Value = 0;
        ImportProgressBar.IsIndeterminate = true;
        SetProgressState("Extracting vehicle…", "AccentBrush");

        try
        {
            var result = await _extractionService.ExtractAsync(_activeEvent, selected);
            ImportProgressBar.IsIndeterminate = false;

            if (!result.Success)
            {
                StatusText.Text = $"Extraction failed for {selected.JobNumber}.";
                DetailText.Text = result.ErrorMessage;
                SetProgressState("Extraction failed", "ErrorBrush");
                MessageBox.Show(result.ErrorMessage, "Vehicle Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
                RefreshImportedJobs(selectedId);
                return;
            }

            ImportProgressBar.Value = 100;
            RefreshImportedJobs(selectedId);
            StatusText.Text = $"Vehicle extraction complete: {selected.JobNumber}";
            DetailText.Text = $"Transparent PNG saved to {result.OutputPath}";
            ExtractionDetailText.Text = "Extraction complete. Review the preview, then click Photo Studio.";
            SetProgressState("✓ Vehicle extraction complete", "SuccessBrush");
        }
        catch (Exception ex)
        {
            ImportProgressBar.IsIndeterminate = false;
            StatusText.Text = "Vehicle extraction stopped unexpectedly.";
            DetailText.Text = ex.Message;
            SetProgressState("Extraction failed", "ErrorBrush");
            MessageBox.Show(ex.Message, "Vehicle Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshImportedJobs(selectedId);
        }
        finally
        {
            ImportProgressBar.IsIndeterminate = false;
            AddLocalPhotosButton.IsEnabled = true;
            UpdateSelectedJobState();
        }
    }

    private void UpdateSelectedJobState()
    {
        var selected = ImportedGrid.SelectedItem as ImportRecord;
        if (selected is null)
        {
            ExtractButton.IsEnabled = false;
            PhotoStudioButton.IsEnabled = false;
            OriginalPreviewImage.Source = null;
            ExtractedPreviewImage.Source = null;
            PreviewStatusText.Text = string.Empty;
            SelectedJobHintText.Text = "Select a job to begin.";
            SetProgressState("Ready", "TextMutedBrush");
            return;
        }

        OriginalPreviewImage.Source = LoadPreview(selected.StoredPath);
        var hasExtraction = !string.IsNullOrWhiteSpace(selected.ExtractionPath) && File.Exists(selected.ExtractionPath);
        ExtractedPreviewImage.Source = hasExtraction ? LoadExtractedPreview(selected.ExtractionPath) : null;
        PreviewStatusText.Text = hasExtraction ? "Ready" : "Not extracted";

        var isBusy = selected.Status.Equals("Extracting", StringComparison.OrdinalIgnoreCase);
        ExtractButton.IsEnabled = !isBusy;
        PhotoStudioButton.IsEnabled = hasExtraction && !isBusy;

        SelectedJobHintText.Text = hasExtraction
            ? $"{selected.JobNumber}: extracted and ready for Photo Studio."
            : $"{selected.JobNumber}: click Extract Selected to remove the current background.";

        if (selected.Status.Equals("Finished", StringComparison.OrdinalIgnoreCase))
        {
            ImportProgressBar.IsIndeterminate = false;
            ImportProgressBar.Value = 100;
            SetProgressState("✓ Finished photo saved", "SuccessBrush");
        }
        else if (hasExtraction)
        {
            ImportProgressBar.IsIndeterminate = false;
            ImportProgressBar.Value = 100;
            SetProgressState("✓ Vehicle extraction complete", "SuccessBrush");
        }
        else if (isBusy)
        {
            ImportProgressBar.IsIndeterminate = true;
            SetProgressState("Extracting vehicle…", "AccentBrush");
        }
        else
        {
            ImportProgressBar.IsIndeterminate = false;
            ImportProgressBar.Value = 0;
            SetProgressState("Ready to extract", "TextMutedBrush");
        }
    }

    private void SetProgressState(string text, string brushKey)
    {
        ExtractionProgressText.Text = text;
        if (TryFindResource(brushKey) is Brush brush)
            ExtractionProgressText.Foreground = brush;
    }

    private static BitmapSource? LoadPreview(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 900;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? LoadExtractedPreview(string path)
    {
        var bitmap = LoadPreview(path);
        if (bitmap is null)
            return null;

        try
        {
            BitmapSource bgra = bitmap;
            if (bitmap.Format != PixelFormats.Bgra32 && bitmap.Format != PixelFormats.Pbgra32)
            {
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                bgra = converted;
            }

            var width = bgra.PixelWidth;
            var height = bgra.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);

            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < height; y++)
            {
                var row = y * stride;
                for (var x = 0; x < width; x++)
                {
                    var alpha = pixels[row + x * 4 + 3];
                    if (alpha <= 12)
                        continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
                return bitmap;

            var visibleWidth = maxX - minX + 1;
            var visibleHeight = maxY - minY + 1;
            var padX = Math.Max(8, (int)(visibleWidth * 0.08));
            var padY = Math.Max(8, (int)(visibleHeight * 0.12));

            minX = Math.Max(0, minX - padX);
            minY = Math.Max(0, minY - padY);
            maxX = Math.Min(width - 1, maxX + padX);
            maxY = Math.Min(height - 1, maxY + padY);

            var crop = new CroppedBitmap(bgra, new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
            crop.Freeze();
            return crop;
        }
        catch
        {
            return bitmap;
        }
    }

    private void OpenExtractedFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(_activeEvent.RootFolder, "04_Extracted");
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true
        });
    }

    private void RefreshImportedJobs(long? reselectId = null)
    {
        var priorId = reselectId ?? (ImportedGrid.SelectedItem as ImportRecord)?.Id;
        _importedJobs.Clear();
        foreach (var record in _importService.GetImportedJobs(_activeEvent.Id))
            _importedJobs.Add(record);

        ImportedCountText.Text = $"{_importedJobs.Count} jobs";
        if (priorId is not null)
            ImportedGrid.SelectedItem = _importedJobs.FirstOrDefault(x => x.Id == priorId.Value);

        UpdateSelectedJobState();
    }

    private static string SafeVolumeLabel(DriveInfo drive)
    {
        try { return drive.VolumeLabel; }
        catch { return "Removable Media"; }
    }

    private sealed record DriveChoice(string RootPath, string DisplayName);
}
