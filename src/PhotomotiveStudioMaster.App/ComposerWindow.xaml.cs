using System.Diagnostics;
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
    private BackgroundRecord? _selectedBackground;
    private BitmapSource? _vehicleBitmap;
    private BitmapSource? _originalBitmap;
    private bool _initialized;
    private bool _showingOriginal;
    private bool _dragging;
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
        LoadBackgrounds();
        _initialized = true;
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

        _vehicleBitmap = LoadBitmap(_job.ExtractionPath);
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
        BackgroundPreviewImage.Source = LoadBitmap(background.FilePath);
        SelectedBackgroundText.Text = background.Name;
        ComposerStatusText.Text = "Background selected. Position the vehicle, compare with the original, then save the finished photo.";
        ComposerDetailText.Text = $"{background.Category}  •  {background.Name}";
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
        ComposerStatusText.Text = _showingOriginal ? "Viewing the original car-show photo." : "Viewing the finished composite preview.";
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

        if (_selectedBackground is null || !File.Exists(_selectedBackground.FilePath))
        {
            MessageBox.Show("Choose a background first.", "Automotive Photo Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
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
        var background = LoadBitmap(_selectedBackground!.FilePath);
        var previewToOutput = OutputWidth / PreviewWidth;

        var drawingVisual = new DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
        {
            var backgroundBrush = new ImageBrush(background)
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

    private static BitmapSource LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
