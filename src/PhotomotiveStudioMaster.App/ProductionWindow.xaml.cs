using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
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
    }

    private void RefreshDrives_Click(object sender, RoutedEventArgs e) => RefreshDrives();

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
            StatusText.Text = "No removable drives detected. Insert the SD card and click Refresh Drives.";
        }
    }

    private void RefreshAiStatus()
    {
        var status = _extractionService.GetRuntimeStatus();
        AiStatusText.Text = status.IsReady ? "● Local AI Ready" : "○ AI Setup Required";
        AiStatusText.ToolTip = status.Message;
        ExtractButton.IsEnabled = status.IsReady;
        ExtractionDetailText.Text = status.IsReady
            ? "Select an imported JPEG/PNG/TIFF job and extract the vehicle."
            : "Run tools\\ai\\Install-AI.ps1 once, then reopen Production.";
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
        DriveCombo.IsEnabled = false;
        ImportProgressBar.Value = 0;

        var progress = new Progress<ImportProgress>(p =>
        {
            ImportProgressBar.Value = p.Total == 0 ? 0 : p.Current * 100.0 / p.Total;
            StatusText.Text = $"{p.Status}: {p.FileName}";
            DetailText.Text = $"File {p.Current} of {p.Total}";
        });

        try
        {
            var result = await _importService.ImportAsync(_activeEvent, _candidates.ToList(), progress);
            RefreshImportedJobs();
            ImportProgressBar.Value = 100;
            StatusText.Text = $"Import complete: {result.Imported} imported, {result.Duplicates} duplicates skipped, {result.Errors} errors.";
            DetailText.Text = "Copied files were SHA-256 verified before being accepted into the event.";

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
            MessageBox.Show(ex.Message, "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImportButton.IsEnabled = _candidates.Count > 0;
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
            MessageBox.Show(runtime.Message, "Local AI Setup Required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ExtractButton.IsEnabled = false;
        StatusText.Text = $"Extracting vehicle from {selected.JobNumber}...";
        DetailText.Text = "Local AI processing is running. The first extraction may take longer while the model initializes.";
        ImportProgressBar.IsIndeterminate = true;

        try
        {
            var result = await _extractionService.ExtractAsync(_activeEvent, selected);
            ImportProgressBar.IsIndeterminate = false;

            if (!result.Success)
            {
                StatusText.Text = $"Extraction failed for {selected.JobNumber}.";
                DetailText.Text = result.ErrorMessage;
                MessageBox.Show(result.ErrorMessage, "Vehicle Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
                RefreshImportedJobs();
                return;
            }

            RefreshImportedJobs();
            StatusText.Text = $"Vehicle extraction complete: {selected.JobNumber}";
            DetailText.Text = $"Transparent PNG saved to {result.OutputPath}";
            ExtractionDetailText.Text = "Extraction complete. Open the extracted folder to inspect the transparent PNG.";
        }
        catch (Exception ex)
        {
            ImportProgressBar.IsIndeterminate = false;
            StatusText.Text = "Vehicle extraction stopped unexpectedly.";
            DetailText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Vehicle Extraction Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImportProgressBar.IsIndeterminate = false;
            ExtractButton.IsEnabled = _extractionService.GetRuntimeStatus().IsReady;
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

    private void RefreshImportedJobs()
    {
        _importedJobs.Clear();
        foreach (var record in _importService.GetImportedJobs(_activeEvent.Id))
            _importedJobs.Add(record);

        ImportedCountText.Text = $"{_importedJobs.Count} jobs";
    }

    private static string SafeVolumeLabel(DriveInfo drive)
    {
        try { return drive.VolumeLabel; }
        catch { return "Removable Media"; }
    }

    private sealed record DriveChoice(string RootPath, string DisplayName);
}
