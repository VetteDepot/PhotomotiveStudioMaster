using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotomotiveStudioMaster.App.Models;
using PhotomotiveStudioMaster.App.Services;

namespace PhotomotiveStudioMaster.App;

public partial class ShowboardComposerWindow : Window
{
    private readonly EventRecord _activeEvent;
    private readonly ImportRecord _job;
    private readonly BackgroundLibraryService _backgroundService = new();
    private IReadOnlyList<BackgroundRecord> _allBackgrounds = Array.Empty<BackgroundRecord>();
    private BackgroundRecord? _selectedBackground;
    private BitmapSource? _vehicleBitmap;
    private bool _initialized;
    private bool _dragging;
    private Point _dragStart;
    private double _dragStartX;
    private double _dragStartY;

    public ShowboardComposerWindow(EventRecord activeEvent, ImportRecord job)
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
            StatusText.Text = "This job does not have an extracted vehicle yet.";
            DetailText.Text = "Return to Production and run Extract Selected first.";
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

        var list = visible.ToList();
        BackgroundList.ItemsSource = list;
        if (BackgroundList.SelectedItem is null && list.Count > 0)
            BackgroundList.SelectedIndex = 0;
    }

    private void BackgroundSearchBox_TextChanged(object sender, TextChangedEventArgs e) => LoadBackgrounds(BackgroundSearchBox.Text);

    private void BackgroundList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackgroundList.SelectedItem is not BackgroundRecord background)
            return;

        _selectedBackground = background;
        BackgroundPreviewImage.Source = LoadBitmap(background.FilePath);
        SelectedBackgroundText.Text = background.Name;
        StatusText.Text = "Background selected. Drag or adjust the vehicle, then export the showboard.";
        DetailText.Text = $"{background.Category}  •  {background.Name}";
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
        YSlider.Value = 80;
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

        const double canvasWidth = 480;
        const double canvasHeight = 720;
        const double baseVehicleWidth = 360;
        var width = baseVehicleWidth * ScaleSlider.Value / 100.0;
        var height = width * _vehicleBitmap.PixelHeight / _vehicleBitmap.PixelWidth;
        CarPreviewImage.Width = width;
        CarPreviewImage.Height = height;
        Canvas.SetLeft(CarPreviewImage, (canvasWidth - width) / 2.0 + XSlider.Value);
        Canvas.SetTop(CarPreviewImage, (canvasHeight - height) / 2.0 + YSlider.Value);
        CarPreviewImage.RenderTransform = new RotateTransform(RotationSlider.Value);
    }

    private void CarPreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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

        var p = e.GetPosition(PreviewCanvas);
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        XSlider.Value = Math.Clamp(_dragStartX + dx, XSlider.Minimum, XSlider.Maximum);
        YSlider.Value = Math.Clamp(_dragStartY + dy, YSlider.Minimum, YSlider.Maximum);
    }

    private void CarPreviewImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        CarPreviewImage.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ExportShowboard_Click(object sender, RoutedEventArgs e)
    {
        if (_vehicleBitmap is null)
        {
            MessageBox.Show("This job does not have an extracted vehicle.", "Showboard Composer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_selectedBackground is null || !File.Exists(_selectedBackground.FilePath))
        {
            MessageBox.Show("Choose a background first.", "Showboard Composer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var folder = Path.Combine(_activeEvent.RootFolder, "06_Showboards");
            Directory.CreateDirectory(folder);
            var outputPath = Path.Combine(folder, _job.JobNumber + "_16x24.png");
            var background = LoadBitmap(_selectedBackground.FilePath);
            const int outputWidth = 4800;
            const int outputHeight = 7200;
            const double scaleFactor = 10.0;

            var drawingVisual = new DrawingVisual();
            using (var dc = drawingVisual.RenderOpen())
            {
                var bgBrush = new ImageBrush(background)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                dc.DrawRectangle(bgBrush, null, new Rect(0, 0, outputWidth, outputHeight));

                var vehicleWidth = 3600.0 * ScaleSlider.Value / 100.0;
                var vehicleHeight = vehicleWidth * _vehicleBitmap.PixelHeight / _vehicleBitmap.PixelWidth;
                var left = (outputWidth - vehicleWidth) / 2.0 + XSlider.Value * scaleFactor;
                var top = (outputHeight - vehicleHeight) / 2.0 + YSlider.Value * scaleFactor;
                var cx = left + vehicleWidth / 2.0;
                var cy = top + vehicleHeight / 2.0;
                dc.PushTransform(new RotateTransform(RotationSlider.Value, cx, cy));
                dc.DrawImage(_vehicleBitmap, new Rect(left, top, vehicleWidth, vehicleHeight));
                dc.Pop();
            }

            var render = new RenderTargetBitmap(outputWidth, outputHeight, 300, 300, PixelFormats.Pbgra32);
            render.Render(drawingVisual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(render));
            using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);

            _backgroundService.MarkUsed(_selectedBackground);
            StatusText.Text = "16×24 showboard exported successfully.";
            DetailText.Text = outputPath;
            MessageBox.Show($"Print-ready 16×24 PNG saved.\n\n{outputPath}", "Showboard Exported", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Showboard export failed.";
            DetailText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Showboard Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenShowboardsFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(_activeEvent.RootFolder, "06_Showboards");
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{folder}\"", UseShellExecute = true });
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