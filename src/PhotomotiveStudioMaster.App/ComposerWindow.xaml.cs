using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotomotiveStudioMaster.App.Models;
using PhotomotiveStudioMaster.App.Services;

namespace PhotomotiveStudioMaster.App;

public partial class ComposerWindow : Window
{
    private readonly EventRecord _activeEvent;
    private readonly ImportRecord _job;
    private readonly BackgroundLibraryService _backgroundService = new();
    private IReadOnlyList<BackgroundRecord> _allBackgrounds = Array.Empty<BackgroundRecord>();
    private BackgroundRecord? _selectedBackground;
    private BitmapSource? _vehicleBitmap;
    private bool _initialized;

    public ComposerWindow(EventRecord activeEvent, ImportRecord job)
    {
        InitializeComponent();
        _activeEvent = activeEvent;
        _job = job;

        JobText.Text = $"{job.JobNumber}  •  {job.OriginalFileName}";
        LoadVehicle();
        LoadBackgrounds();
        _initialized = true;
        UpdatePlacementPreview();
    }

    private void LoadVehicle()
    {
        if (string.IsNullOrWhiteSpace(_job.ExtractionPath) || !File.Exists(_job.ExtractionPath))
        {
            ComposerStatusText.Text = "This job does not have an extracted vehicle yet.";
            ComposerDetailText.Text = "Return to Production and run Extract Selected first.";
            return;
        }

        _vehicleBitmap = LoadBitmap(_job.ExtractionPath);
        CarPreviewImage.Source = _vehicleBitmap;
    }

    private void LoadBackgrounds(string? search = null)
    {
        _allBackgrounds = _backgroundService.GetAll();
        IEnumerable<BackgroundRecord> visible = _allBackgrounds;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            visible = visible.Where(x =>
                x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.Tags.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        BackgroundList.ItemsSource = visible.ToList();
        if (BackgroundList.SelectedItem is null && visible.Any())
            BackgroundList.SelectedIndex = 0;
    }

    private void BackgroundSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        LoadBackgrounds(BackgroundSearchBox.Text);
    }

    private void BackgroundList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackgroundList.SelectedItem is not BackgroundRecord background)
            return;

        _selectedBackground = background;
        BackgroundPreviewImage.Source = LoadBitmap(background.FilePath);
        SelectedBackgroundText.Text = background.Name;
        ComposerStatusText.Text = "Background selected. Adjust the vehicle placement, then save the composite.";
        ComposerDetailText.Text = $"{background.Category}  •  {background.Name}";
        _backgroundService.MarkUsed(background);
    }

    private void PlacementSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initialized)
            UpdatePlacementPreview();
    }

    private void ResetPlacement_Click(object sender, RoutedEventArgs e)
    {
        ScaleSlider.Value = 100;
        XSlider.Value = 0;
        YSlider.Value = 0;
        RotationSlider.Value = 0;
        UpdatePlacementPreview();
    }

    private void UpdatePlacementPreview()
    {
        ScaleValueText.Text = $"{ScaleSlider.Value:0}%";
        XValueText.Text = $"{XSlider.Value:+0;-0;0}";
        YValueText.Text = $"{YSlider.Value:+0;-0;0}";
        RotationValueText.Text = $"{RotationSlider.Value:+0;-0;0}°";

        if (_vehicleBitmap is null)
            return;

        const double previewWidth = 800;
        const double previewHeight = 640;
        const double baseVehicleWidth = 560;
        var width = baseVehicleWidth * ScaleSlider.Value / 100.0;
        var height = width * _vehicleBitmap.PixelHeight / _vehicleBitmap.PixelWidth;

        CarPreviewImage.Width = width;
        CarPreviewImage.Height = height;
        Canvas.SetLeft(CarPreviewImage, (previewWidth - width) / 2.0 + XSlider.Value);
        Canvas.SetTop(CarPreviewImage, (previewHeight - height) / 2.0 + 100 + YSlider.Value);
        CarPreviewImage.RenderTransform = new RotateTransform(RotationSlider.Value);
    }

    private void SaveComposite_Click(object sender, RoutedEventArgs e)
    {
        if (_vehicleBitmap is null)
        {
            MessageBox.Show("This job does not have an extracted vehicle to composite.", "Live Composer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_selectedBackground is null || !File.Exists(_selectedBackground.FilePath))
        {
            MessageBox.Show("Choose a background first.", "Live Composer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var outputFolder = Path.Combine(_activeEvent.RootFolder, "05_Composites");
            Directory.CreateDirectory(outputFolder);
            var outputPath = Path.Combine(outputFolder, _job.JobNumber + "_8x10.png");

            var background = LoadBitmap(_selectedBackground.FilePath);
            const int outputWidth = 3000;
            const int outputHeight = 2400;
            const double previewToOutput = outputWidth / 800.0;

            var drawingVisual = new DrawingVisual();
            using (var dc = drawingVisual.RenderOpen())
            {
                var backgroundBrush = new ImageBrush(background)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                dc.DrawRectangle(backgroundBrush, null, new Rect(0, 0, outputWidth, outputHeight));

                var vehicleWidth = 2100.0 * ScaleSlider.Value / 100.0;
                var vehicleHeight = vehicleWidth * _vehicleBitmap.PixelHeight / _vehicleBitmap.PixelWidth;
                var left = (outputWidth - vehicleWidth) / 2.0 + XSlider.Value * previewToOutput;
                var top = (outputHeight - vehicleHeight) / 2.0 + 375 + YSlider.Value * previewToOutput;
                var centerX = left + vehicleWidth / 2.0;
                var centerY = top + vehicleHeight / 2.0;

                dc.PushTransform(new RotateTransform(RotationSlider.Value, centerX, centerY));
                dc.DrawImage(_vehicleBitmap, new Rect(left, top, vehicleWidth, vehicleHeight));
                dc.Pop();
            }

            var render = new RenderTargetBitmap(outputWidth, outputHeight, 300, 300, PixelFormats.Pbgra32);
            render.Render(drawingVisual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(render));
            using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                encoder.Save(stream);

            _backgroundService.MarkUsed(_selectedBackground);
            ComposerStatusText.Text = $"Composite saved: {_job.JobNumber}";
            ComposerDetailText.Text = outputPath;

            MessageBox.Show(
                $"Print-ready 8×10 composite saved successfully.\n\n{outputPath}",
                "Composite Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ComposerStatusText.Text = "Composite export failed.";
            ComposerDetailText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Composite Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenCompositesFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(_activeEvent.RootFolder, "05_Composites");
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{folder}\"",
            UseShellExecute = true
        });
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
