using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotomotiveStudioMaster.App.Models;
using PhotomotiveStudioMaster.App.Services;

namespace PhotomotiveStudioMaster.App;

public partial class ComposerWindow : Window
{
    private const double PreviewWidth = 800;
    private const double PreviewHeight = 640;
    private const double BaseVehicleWidth = 560;
    private const int OutputWidth = 3000;
    private const int OutputHeight = 2400;

    private readonly EventRecord _activeEvent;
    private readonly ImportRecord _job;
    private readonly BackgroundLibraryService _backgroundService = new();
    private readonly ImportRepository _importRepository = new();
    private BackgroundRecord? _selectedBackground;
    private BitmapSource? _vehicleBitmap;
    private BitmapSource? _originalBitmap;
    private BitmapSource? _backgroundBitmap;
    private bool _initialized;
    private bool _showingOriginal;
    private bool _dragging;
    private bool _restoringProject;
    private Point _dragStart;
    private double _dragStartX;
    private double _dragStartY;

    public ComposerWindow(EventRecord activeEvent, ImportRecord job)
    {
        InitializeComponent();
        _activeEvent = activeEvent;
        _job = job;

        JobText.Text = $"{job.JobNumber}  •  {job.OriginalFileName}";
        LoadVehicleAndOriginal();
        LoadCategories();
        _initialized = true;
        LoadBackgrounds();

        if (!TryRestoreProject())
            AutoPosition();
    }

    private void LoadVehicleAndOriginal()
    {
        if (!string.IsNullOrWhiteSpace(_job.StoredPath) && File.Exists(_job.StoredPath))
        {
            _originalBitmap = LoadBitmap(_job.StoredPath);
            OriginalPreviewImage.Source = _originalBitmap;
        }

        if (string.IsNullOrWhiteSpace(_job.ExtractionPath) || !File.Exists(_job.ExtractionPath))
        {
            ComposerStatusText.Text = "This job does not have an extracted vehicle yet.";
            ComposerDetailText.Text = "Return to Production and run Extract Selected first.";
            return;
        }

        var extracted = LoadBitmap(_job.ExtractionPath);
        _vehicleBitmap = CropToVisibleAlpha(extracted);
        CarPreviewImage.Source = _vehicleBitmap;
    }

    private void LoadCategories()
    {
        CategoryFilter.ItemsSource = _backgroundService.GetCategories();
        CategoryFilter.SelectedItem = "All";
    }

    private void LoadBackgrounds()
    {
        var category = CategoryFilter.SelectedItem?.ToString() ?? "All";
        var visible = _backgroundService.Filter(BackgroundSearchBox.Text, category);
        BackgroundList.ItemsSource = visible;
        BackgroundCountText.Text = $"{visible.Count} shown";

        if (visible.Count > 0 && BackgroundList.SelectedItem is null)
            BackgroundList.SelectedIndex = 0;
    }

    private void BackgroundSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized)
            LoadBackgrounds();
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initialized)
            LoadBackgrounds();
    }

    private void BackgroundList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackgroundList.SelectedItem is not BackgroundRecord background)
            return;

        _selectedBackground = background;
        _backgroundBitmap = LoadBitmap(background.FilePath);
        BackgroundPreviewImage.Source = _backgroundBitmap;
        SelectedBackgroundText.Text = background.Name;
        ComposerStatusText.Text = "Background selected. Fine-tune the vehicle or save the finished photo.";
        ComposerDetailText.Text = $"{background.Category}  •  {background.Name}  •  {background.ResolutionDisplay}";

        if (!_restoringProject)
            _backgroundService.MarkUsed(background);
    }

    private void PlacementSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initialized)
            UpdatePlacementPreview();
    }

    private void AutoPosition_Click(object sender, RoutedEventArgs e) => AutoPosition();

    private void AutoFit_Click(object sender, RoutedEventArgs e)
    {
        AutoFitVehicle();
        UpdatePlacementPreview();
        ComposerStatusText.Text = "Vehicle fitted to the scene. Drag it to fine-tune placement.";
    }

    private void AutoPosition()
    {
        AutoFitVehicle();
        XSlider.Value = 0;
        YSlider.Value = 95;
        RotationSlider.Value = 0;
        UpdatePlacementPreview();
        ComposerStatusText.Text = "Vehicle automatically positioned. Fine-tune by dragging if needed.";
    }

    private void AutoFitVehicle()
    {
        if (_vehicleBitmap is null)
        {
            ScaleSlider.Value = 100;
            return;
        }

        const double targetWidth = 600;
        const double maxHeight = 430;
        var widthScale = targetWidth / BaseVehicleWidth * 100.0;
        var projectedHeight = targetWidth * _vehicleBitmap.PixelHeight / _vehicleBitmap.PixelWidth;
        var heightScale = projectedHeight <= maxHeight
            ? widthScale
            : widthScale * maxHeight / projectedHeight;

        ScaleSlider.Value = Math.Clamp(heightScale, ScaleSlider.Minimum, ScaleSlider.Maximum);
    }

    private void ResetPlacement_Click(object sender, RoutedEventArgs e)
    {
        ScaleSlider.Value = 100;
        XSlider.Value = 0;
        YSlider.Value = 90;
        RotationSlider.Value = 0;
        UpdatePlacementPreview();
        ComposerStatusText.Text = "Vehicle placement reset.";
    }

    private void UpdatePlacementPreview()
    {
        ScaleValueText.Text = $"{ScaleSlider.Value:0}%";
        XValueText.Text = $"{XSlider.Value:+0;-0;0}";
        YValueText.Text = $"{YSlider.Value:+0;-0;0}";
        RotationValueText.Text = $"{RotationSlider.Value:+0;-0;0}°";

        if (_vehicleBitmap is null)
            return;

        var width = BaseVehicleWidth * ScaleSlider.Value / 100.0;
        var height = width * _vehicleBitmap.PixelHeight / _vehicleBitmap.PixelWidth;

        CarPreviewImage.Width = width;
        CarPreviewImage.Height = height;
        Canvas.SetLeft(CarPreviewImage, (PreviewWidth - width) / 2.0 + XSlider.Value);
        Canvas.SetTop(CarPreviewImage, (PreviewHeight - height) / 2.0 + YSlider.Value);
        CarPreviewImage.RenderTransform = new RotateTransform(RotationSlider.Value);
    }

    private void CarPreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_showingOriginal)
            return;

        _dragging = true;
        _dragStart = e.GetPosition(PreviewCanvas);
        _dragStartX = XSlider.Value;
        _dragStartY = YSlider.Value;
        CarPreviewImage.CaptureMouse();
        e.Handled = true;
    }

    private void CarPreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
            return;

        var point = e.GetPosition(PreviewCanvas);
        XSlider.Value = Math.Clamp(_dragStartX + point.X - _dragStart.X, XSlider.Minimum, XSlider.Maximum);
        YSlider.Value = Math.Clamp(_dragStartY + point.Y - _dragStart.Y, YSlider.Minimum, YSlider.Maximum);
        e.Handled = true;
    }

    private void CarPreviewImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        CarPreviewImage.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void PreviewCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_showingOriginal)
            return;

        var step = e.Delta > 0 ? 5 : -5;
        ScaleSlider.Value = Math.Clamp(ScaleSlider.Value + step, ScaleSlider.Minimum, ScaleSlider.Maximum);
        e.Handled = true;
    }

    private void CompareButton_Click(object sender, RoutedEventArgs e) => ToggleCompare();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            ToggleCompare();
            e.Handled = true;
        }
        else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SaveProject(showConfirmation: true);
            e.Handled = true;
        }
    }

    private void ToggleCompare()
    {
        if (_originalBitmap is null)
        {
            ComposerStatusText.Text = "Original photo is not available for comparison.";
            return;
        }

        _showingOriginal = !_showingOriginal;
        OriginalPreviewImage.Visibility = _showingOriginal ? Visibility.Visible : Visibility.Collapsed;
        CompareBadge.Visibility = _showingOriginal ? Visibility.Visible : Visibility.Collapsed;
        CompareButton.Content = _showingOriginal ? "SHOW FINISHED" : "COMPARE ORIGINAL";
        ComposerStatusText.Text = _showingOriginal ? "Viewing the original car-show photo." : "Viewing the finished photo preview.";
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e) => SaveProject(showConfirmation: true);

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        try { SaveProject(showConfirmation: false); }
        catch { }
    }

    private void SaveProject(bool showConfirmation)
    {
        var state = new PhotoStudioProjectState
        {
            JobId = _job.Id,
            JobNumber = _job.JobNumber,
            BackgroundId = _selectedBackground?.Id ?? 0,
            BackgroundName = _selectedBackground?.Name ?? string.Empty,
            Scale = ScaleSlider.Value,
            X = XSlider.Value,
            Y = YSlider.Value,
            Rotation = RotationSlider.Value,
            SavedAt = DateTime.Now
        };

        var folder = GetProjectFolder();
        var path = Path.Combine(folder, _job.JobNumber + ".json");
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);

        ComposerStatusText.Text = "Project saved.";
        ComposerDetailText.Text = path;

        if (showConfirmation)
        {
            MessageBox.Show(
                "Photo Studio project saved. Your background and vehicle placement will be restored the next time you open this job.",
                "Project Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private bool TryRestoreProject()
    {
        var path = Path.Combine(GetProjectFolder(), _job.JobNumber + ".json");
        if (!File.Exists(path))
            return false;

        try
        {
            var state = JsonSerializer.Deserialize<PhotoStudioProjectState>(File.ReadAllText(path));
            if (state is null)
                return false;

            _restoringProject = true;
            CategoryFilter.SelectedItem = "All";
            LoadBackgrounds();

            var backgrounds = BackgroundList.ItemsSource?.Cast<BackgroundRecord>().ToList() ?? new List<BackgroundRecord>();
            var savedBackground = backgrounds.FirstOrDefault(x => x.Id == state.BackgroundId);
            if (savedBackground is not null)
                BackgroundList.SelectedItem = savedBackground;

            ScaleSlider.Value = Math.Clamp(state.Scale, ScaleSlider.Minimum, ScaleSlider.Maximum);
            XSlider.Value = Math.Clamp(state.X, XSlider.Minimum, XSlider.Maximum);
            YSlider.Value = Math.Clamp(state.Y, YSlider.Minimum, YSlider.Maximum);
            RotationSlider.Value = Math.Clamp(state.Rotation, RotationSlider.Minimum, RotationSlider.Maximum);
            UpdatePlacementPreview();

            ComposerStatusText.Text = "Saved Photo Studio project restored.";
            ComposerDetailText.Text = state.SavedAt == default
                ? _job.JobNumber
                : $"Last saved {state.SavedAt:g}";
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _restoringProject = false;
        }
    }

    private void SaveJpg_Click(object sender, RoutedEventArgs e) => SaveFinishedPhoto("jpg");
    private void SavePng_Click(object sender, RoutedEventArgs e) => SaveFinishedPhoto("png");
    private void SaveTiff_Click(object sender, RoutedEventArgs e) => SaveFinishedPhoto("tif");

    private void SaveFinishedPhoto(string extension)
    {
        if (_vehicleBitmap is null)
        {
            MessageBox.Show("This job does not have an extracted vehicle to finish.", "Automotive Photo Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_selectedBackground is null || _backgroundBitmap is null || !File.Exists(_selectedBackground.FilePath))
        {
            MessageBox.Show("Choose a background first.", "Automotive Photo Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            SaveProject(showConfirmation: false);
            var outputFolder = GetFinishedFolder();
            var outputPath = Path.Combine(outputFolder, $"{_job.JobNumber}_Finished.{extension}");
            var render = RenderFinishedFrame();

            BitmapEncoder encoder = extension switch
            {
                "jpg" => new JpegBitmapEncoder { QualityLevel = 95 },
                "tif" => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
                _ => new PngBitmapEncoder()
            };

            encoder.Frames.Add(BitmapFrame.Create(render));
            using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                encoder.Save(stream);

            _backgroundService.MarkUsed(_selectedBackground);
            _importRepository.UpdateStatus(_job.Id, "Finished");
            _job.Status = "Finished";
            ComposerStatusText.Text = $"Finished photo saved: {_job.JobNumber}";
            ComposerDetailText.Text = outputPath;

            MessageBox.Show(
                $"Finished 8×10 photo saved successfully.\n\n{outputPath}",
                "Photo Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ComposerStatusText.Text = "Finished photo export failed.";
            ComposerDetailText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Photo Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private RenderTargetBitmap RenderFinishedFrame()
    {
        var previewToOutput = OutputWidth / PreviewWidth;
        var drawingVisual = new DrawingVisual();

        using (var dc = drawingVisual.RenderOpen())
        {
            var backgroundBrush = new ImageBrush(_backgroundBitmap!)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            dc.DrawRectangle(backgroundBrush, null, new Rect(0, 0, OutputWidth, OutputHeight));

            var vehicleWidth = BaseVehicleWidth * previewToOutput * ScaleSlider.Value / 100.0;
            var vehicleHeight = vehicleWidth * _vehicleBitmap!.PixelHeight / _vehicleBitmap.PixelWidth;
            var left = (OutputWidth - vehicleWidth) / 2.0 + XSlider.Value * previewToOutput;
            var top = (OutputHeight - vehicleHeight) / 2.0 + YSlider.Value * previewToOutput;
            var centerX = left + vehicleWidth / 2.0;
            var centerY = top + vehicleHeight / 2.0;

            dc.PushTransform(new RotateTransform(RotationSlider.Value, centerX, centerY));
            dc.DrawImage(_vehicleBitmap, new Rect(left, top, vehicleWidth, vehicleHeight));
            dc.Pop();
        }

        var render = new RenderTargetBitmap(OutputWidth, OutputHeight, 300, 300, PixelFormats.Pbgra32);
        render.Render(drawingVisual);
        return render;
    }

    private void OpenFinishedFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = GetFinishedFolder();
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true
        });
    }

    private string GetFinishedFolder()
    {
        var folder = Path.Combine(_activeEvent.RootFolder, "05_Finished");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private string GetProjectFolder()
    {
        var folder = Path.Combine(_activeEvent.RootFolder, "StudioProjects");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static BitmapSource CropToVisibleAlpha(BitmapSource source)
    {
        try
        {
            BitmapSource bgra = source;
            if (source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32)
            {
                var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
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
                    if (pixels[row + x * 4 + 3] <= 8)
                        continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
                return source;

            var visibleWidth = maxX - minX + 1;
            var visibleHeight = maxY - minY + 1;
            var padX = Math.Max(4, (int)(visibleWidth * 0.02));
            var padY = Math.Max(4, (int)(visibleHeight * 0.03));
            minX = Math.Max(0, minX - padX);
            minY = Math.Max(0, minY - padY);
            maxX = Math.Min(width - 1, maxX + padX);
            maxY = Math.Min(height - 1, maxY + padY);

            var cropped = new CroppedBitmap(bgra, new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
            cropped.Freeze();
            return cropped;
        }
        catch
        {
            return source;
        }
    }

    private sealed class PhotoStudioProjectState
    {
        public long JobId { get; set; }
        public string JobNumber { get; set; } = string.Empty;
        public long BackgroundId { get; set; }
        public string BackgroundName { get; set; } = string.Empty;
        public double Scale { get; set; } = 100;
        public double X { get; set; }
        public double Y { get; set; } = 90;
        public double Rotation { get; set; }
        public DateTime SavedAt { get; set; }
    }
}
