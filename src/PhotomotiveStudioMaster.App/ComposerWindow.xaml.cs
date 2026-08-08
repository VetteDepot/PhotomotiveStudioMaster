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
        {
            AutoPosition();
            AutoShadow(showStatus: false);
        }
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
        ComposerStatusText.Text = "Background selected. Fine-tune the vehicle and shadow or save the finished photo.";
        ComposerDetailText.Text = $"{background.Category}  •  {background.Name}  •  {background.ResolutionDisplay}";

        if (!_restoringProject)
            _backgroundService.MarkUsed(background);
    }

    private void PlacementSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initialized)
            UpdatePlacementPreview();
    }

    private void ShadowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initialized)
            UpdateShadowPreview();
    }

    private void ShadowControl_Changed(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            UpdateShadowPreview();
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

        var rect = GetVehicleRect(1.0);
        CarPreviewImage.Width = rect.Width;
        CarPreviewImage.Height = rect.Height;
        Canvas.SetLeft(CarPreviewImage, rect.Left);
        Canvas.SetTop(CarPreviewImage, rect.Top);
        CarPreviewImage.RenderTransform = new RotateTransform(RotationSlider.Value);
        UpdateShadowPreview();
        UpdateReflectionPreview();
    }

    private Rect GetVehicleRect(double scaleFactor)
    {
        if (_vehicleBitmap is null)
            return Rect.Empty;

        var canvasWidth = PreviewWidth * scaleFactor;
        var canvasHeight = PreviewHeight * scaleFactor;
        var width = BaseVehicleWidth * scaleFactor * ScaleSlider.Value / 100.0;
        var height = width * _vehicleBitmap.PixelHeight / _vehicleBitmap.PixelWidth;
        var left = (canvasWidth - width) / 2.0 + XSlider.Value * scaleFactor;
        var top = (canvasHeight - height) / 2.0 + YSlider.Value * scaleFactor;
        return new Rect(left, top, width, height);
    }

    private void AutoShadow_Click(object sender, RoutedEventArgs e) => AutoShadow(showStatus: true);

    private void AutoShadow(bool showStatus)
    {
        ShadowEnabledCheckBox.IsChecked = true;
        ShadowOpacitySlider.Value = 34;
        ShadowSoftnessSlider.Value = 20;
        ShadowWidthSlider.Value = 88;

        if (_vehicleBitmap is not null)
        {
            var aspect = _vehicleBitmap.PixelWidth / (double)Math.Max(1, _vehicleBitmap.PixelHeight);
            ShadowLengthSlider.Value = aspect >= 2.2 ? 38 : aspect >= 1.7 ? 44 : 50;
        }
        else
        {
            ShadowLengthSlider.Value = 44;
        }

        ShadowAngleSlider.Value = 0;
        ShadowXSlider.Value = 0;
        ShadowYSlider.Value = 0;
        ContactShadowSlider.Value = 68;
        UpdateShadowPreview();

        if (showStatus)
        {
            ComposerStatusText.Text = "Auto Shadow anchored to the detected tire contact points.";
            ComposerDetailText.Text = "The front and rear tire contacts are detected from the vehicle alpha edge; Angle now acts as a fine adjustment from that natural tire line.";
        }
    }

    private void ResetShadow_Click(object sender, RoutedEventArgs e)
    {
        ShadowEnabledCheckBox.IsChecked = true;
        ShadowOpacitySlider.Value = 32;
        ShadowSoftnessSlider.Value = 20;
        ShadowWidthSlider.Value = 85;
        ShadowLengthSlider.Value = 42;
        ShadowAngleSlider.Value = 0;
        ShadowXSlider.Value = 0;
        ShadowYSlider.Value = 0;
        ContactShadowSlider.Value = 60;
        UpdateShadowPreview();
        ComposerStatusText.Text = "Shadow controls reset.";
    }

    private void UpdateShadowPreview()
    {
        ShadowOpacityValueText.Text = $"{ShadowOpacitySlider.Value:0}%";
        ShadowSoftnessValueText.Text = $"{ShadowSoftnessSlider.Value:0}";
        ShadowWidthValueText.Text = $"{ShadowWidthSlider.Value:0}%";
        ShadowLengthValueText.Text = $"{ShadowLengthSlider.Value:0}%";
        ShadowAngleValueText.Text = $"{ShadowAngleSlider.Value:+0;-0;0}°";
        ShadowXValueText.Text = $"{ShadowXSlider.Value:+0;-0;0}";
        ShadowYValueText.Text = $"{ShadowYSlider.Value:+0;-0;0}";
        ContactShadowValueText.Text = $"{ContactShadowSlider.Value:0}%";

        var enabled = ShadowEnabledCheckBox.IsChecked == true && _vehicleBitmap is not null;
        ShadowCanvas.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled)
            return;

        var vehicle = GetVehicleRect(1.0);
        if (vehicle.IsEmpty)
            return;

        var geometry = GetShadowGeometry(vehicle, 1.0);
        GroundShadowEllipse.Width = geometry.Ground.Width;
        GroundShadowEllipse.Height = geometry.Ground.Height;
        GroundShadowEllipse.Opacity = ShadowOpacitySlider.Value / 100.0;
        GroundShadowBlur.Radius = ShadowSoftnessSlider.Value;
        GroundShadowEllipse.RenderTransformOrigin = new Point(0.5, 0.5);
        GroundShadowEllipse.RenderTransform = new RotateTransform(geometry.AngleDegrees);
        Canvas.SetLeft(GroundShadowEllipse, geometry.Ground.Left);
        Canvas.SetTop(GroundShadowEllipse, geometry.Ground.Top);

        var contactOpacity = ContactShadowSlider.Value / 100.0 * 0.82;
        LeftContactShadow.Width = geometry.LeftContact.Width;
        LeftContactShadow.Height = geometry.LeftContact.Height;
        LeftContactShadow.Opacity = contactOpacity;
        Canvas.SetLeft(LeftContactShadow, geometry.LeftContact.Left);
        Canvas.SetTop(LeftContactShadow, geometry.LeftContact.Top);

        RightContactShadow.Width = geometry.RightContact.Width;
        RightContactShadow.Height = geometry.RightContact.Height;
        RightContactShadow.Opacity = contactOpacity;
        Canvas.SetLeft(RightContactShadow, geometry.RightContact.Left);
        Canvas.SetTop(RightContactShadow, geometry.RightContact.Top);
    }

    private ShadowGeometry GetShadowGeometry(Rect vehicle, double scaleFactor)
    {
        var contacts = DetectTireContacts(_vehicleBitmap);

        var rearX = vehicle.Left + vehicle.Width * contacts.LeftX + ShadowXSlider.Value * scaleFactor;
        var frontX = vehicle.Left + vehicle.Width * contacts.RightX + ShadowXSlider.Value * scaleFactor;
        var rearY = vehicle.Top + vehicle.Height * contacts.LeftY + ShadowYSlider.Value * scaleFactor;
        var frontY = vehicle.Top + vehicle.Height * contacts.RightY + ShadowYSlider.Value * scaleFactor;

        var dx = Math.Max(1.0, frontX - rearX);
        var naturalAngle = Math.Atan2(frontY - rearY, dx) * 180.0 / Math.PI;
        var finalAngle = naturalAngle + ShadowAngleSlider.Value;

        var axleSpan = Math.Max(vehicle.Width * 0.30, frontX - rearX);
        var widthFactor = Math.Clamp(ShadowWidthSlider.Value / 100.0, 0.35, 1.35);
        var groundWidth = axleSpan * (1.08 + 0.34 * widthFactor);
        var groundHeight = vehicle.Height * (0.055 + ShadowLengthSlider.Value / 100.0 * 0.22);
        var centerX = (rearX + frontX) / 2.0;
        var centerY = (rearY + frontY) / 2.0 + groundHeight * 0.12;
        var ground = new Rect(centerX - groundWidth / 2.0, centerY - groundHeight / 2.0, groundWidth, groundHeight);

        var contactWidth = Math.Max(12 * scaleFactor, vehicle.Width * 0.115);
        var contactHeight = Math.Max(4 * scaleFactor, vehicle.Height * 0.025);
        var leftContact = new Rect(rearX - contactWidth / 2.0, rearY - contactHeight * 0.42, contactWidth, contactHeight);
        var rightContact = new Rect(frontX - contactWidth / 2.0, frontY - contactHeight * 0.42, contactWidth, contactHeight);

        return new ShadowGeometry(ground, leftContact, rightContact, finalAngle);
    }

    private static TireContactRatios DetectTireContacts(BitmapSource? source)
    {
        if (source is null)
            return new TireContactRatios(0.20, 0.62, 0.94, 0.94);

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

            var bottom = new int[width];
            Array.Fill(bottom, -1);
            var absoluteBottom = -1;

            for (var x = 0; x < width; x++)
            {
                for (var y = height - 1; y >= 0; y--)
                {
                    if (pixels[y * stride + x * 4 + 3] < 48)
                        continue;

                    bottom[x] = y;
                    absoluteBottom = Math.Max(absoluteBottom, y);
                    break;
                }
            }

            if (absoluteBottom < 0)
                return new TireContactRatios(0.20, 0.62, 0.94, 0.94);

            var tolerance = Math.Max(3, (int)Math.Round(height * 0.075));
            var candidate = new bool[width];
            for (var x = 0; x < width; x++)
                candidate[x] = bottom[x] >= absoluteBottom - tolerance;

            var clusters = new List<(int Start, int End, int PeakY, double Score)>();
            var start = -1;
            for (var x = 0; x <= width; x++)
            {
                var active = x < width && candidate[x];
                if (active && start < 0)
                {
                    start = x;
                    continue;
                }

                if (active || start < 0)
                    continue;

                var end = x - 1;
                var runWidth = end - start + 1;
                if (runWidth >= Math.Max(3, width / 120))
                {
                    var peakY = -1;
                    var depthSum = 0.0;
                    for (var xx = start; xx <= end; xx++)
                    {
                        peakY = Math.Max(peakY, bottom[xx]);
                        if (bottom[xx] >= 0)
                            depthSum += bottom[xx];
                    }

                    var avgDepth = depthSum / Math.Max(1, runWidth);
                    var score = runWidth * 1.8 + avgDepth / Math.Max(1, height) * width * 0.10;
                    clusters.Add((start, end, peakY, score));
                }

                start = -1;
            }

            if (clusters.Count < 2)
                return new TireContactRatios(0.20, 0.62, 0.94, 0.94);

            var bestPair = (A: clusters[0], B: clusters[1], Score: double.MinValue);
            for (var i = 0; i < clusters.Count; i++)
            {
                for (var j = i + 1; j < clusters.Count; j++)
                {
                    var aCenter = (clusters[i].Start + clusters[i].End) / 2.0;
                    var bCenter = (clusters[j].Start + clusters[j].End) / 2.0;
                    var separation = Math.Abs(bCenter - aCenter) / Math.Max(1.0, width);
                    if (separation < 0.22)
                        continue;

                    var pairScore = clusters[i].Score + clusters[j].Score + separation * width * 0.65;
                    if (pairScore > bestPair.Score)
                        bestPair = (clusters[i], clusters[j], pairScore);
                }
            }

            if (bestPair.Score == double.MinValue)
                return new TireContactRatios(0.20, 0.62, 0.94, 0.94);

            var first = bestPair.A;
            var second = bestPair.B;
            if (first.Start > second.Start)
                (first, second) = (second, first);

            var leftX = ((first.Start + first.End) / 2.0) / Math.Max(1, width - 1);
            var rightX = ((second.Start + second.End) / 2.0) / Math.Max(1, width - 1);
            var leftY = first.PeakY / (double)Math.Max(1, height - 1);
            var rightY = second.PeakY / (double)Math.Max(1, height - 1);

            return new TireContactRatios(
                Math.Clamp(leftX, 0.08, 0.48),
                Math.Clamp(rightX, 0.42, 0.92),
                Math.Clamp(leftY, 0.80, 0.995),
                Math.Clamp(rightY, 0.80, 0.995));
        }
        catch
        {
            return new TireContactRatios(0.20, 0.62, 0.94, 0.94);
        }
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
        UpdateReflectionPreview();
        ComposerStatusText.Text = _showingOriginal ? "Viewing the original car-show photo." : "Viewing the finished photo preview.";
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e) => SaveProject(showConfirmation: true);

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            SaveProject(showConfirmation: false);
            SaveReflectionState();
        }
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
            ShadowEnabled = ShadowEnabledCheckBox.IsChecked == true,
            ShadowOpacity = ShadowOpacitySlider.Value,
            ShadowSoftness = ShadowSoftnessSlider.Value,
            ShadowWidth = ShadowWidthSlider.Value,
            ShadowLength = ShadowLengthSlider.Value,
            ShadowAngle = ShadowAngleSlider.Value,
            ShadowX = ShadowXSlider.Value,
            ShadowY = ShadowYSlider.Value,
            ContactShadow = ContactShadowSlider.Value,
            SavedAt = DateTime.Now
        };

        var folder = GetProjectFolder();
        var path = Path.Combine(folder, _job.JobNumber + ".json");
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        SaveReflectionState();

        ComposerStatusText.Text = "Project saved.";
        ComposerDetailText.Text = path;

        if (showConfirmation)
        {
            MessageBox.Show(
                "Photo Studio project saved. Your background, vehicle placement, shadow, reflection, halo and realism settings will be restored the next time you open this job.",
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

            ShadowEnabledCheckBox.IsChecked = state.ShadowEnabled;
            ShadowOpacitySlider.Value = Math.Clamp(state.ShadowOpacity, ShadowOpacitySlider.Minimum, ShadowOpacitySlider.Maximum);
            ShadowSoftnessSlider.Value = Math.Clamp(state.ShadowSoftness, ShadowSoftnessSlider.Minimum, ShadowSoftnessSlider.Maximum);
            ShadowWidthSlider.Value = Math.Clamp(state.ShadowWidth, ShadowWidthSlider.Minimum, ShadowWidthSlider.Maximum);
            ShadowLengthSlider.Value = Math.Clamp(state.ShadowLength, ShadowLengthSlider.Minimum, ShadowLengthSlider.Maximum);
            ShadowAngleSlider.Value = Math.Clamp(state.ShadowAngle, ShadowAngleSlider.Minimum, ShadowAngleSlider.Maximum);
            ShadowXSlider.Value = Math.Clamp(state.ShadowX, ShadowXSlider.Minimum, ShadowXSlider.Maximum);
            ShadowYSlider.Value = Math.Clamp(state.ShadowY, ShadowYSlider.Minimum, ShadowYSlider.Maximum);
            ContactShadowSlider.Value = Math.Clamp(state.ContactShadow, ContactShadowSlider.Minimum, ContactShadowSlider.Maximum);

            UpdatePlacementPreview();
            UpdateShadowPreview();

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

            var vehicle = GetVehicleRect(previewToOutput);
            DrawExportReflection(dc, vehicle, previewToOutput);

            if (ShadowEnabledCheckBox.IsChecked == true)
                DrawExportShadow(dc, vehicle, previewToOutput);

            var centerX = vehicle.Left + vehicle.Width / 2.0;
            var centerY = vehicle.Top + vehicle.Height / 2.0;
            dc.PushTransform(new RotateTransform(RotationSlider.Value, centerX, centerY));
            dc.DrawImage(_vehicleBitmap, vehicle);
            dc.Pop();
        }

        var render = new RenderTargetBitmap(OutputWidth, OutputHeight, 300, 300, PixelFormats.Pbgra32);
        render.Render(drawingVisual);
        return render;
    }

    private void DrawExportShadow(DrawingContext dc, Rect vehicle, double scaleFactor)
    {
        var geometry = GetShadowGeometry(vehicle, scaleFactor);
        var opacity = Math.Clamp(ShadowOpacitySlider.Value / 100.0, 0, 0.9);
        var softness = Math.Clamp(ShadowSoftnessSlider.Value / 45.0, 0.05, 1.0);

        var groundBrush = CreateSoftShadowBrush(opacity, softness);
        var groundCenter = new Point(geometry.Ground.Left + geometry.Ground.Width / 2.0, geometry.Ground.Top + geometry.Ground.Height / 2.0);
        dc.PushTransform(new RotateTransform(geometry.AngleDegrees, groundCenter.X, groundCenter.Y));
        dc.DrawEllipse(groundBrush, null, groundCenter, geometry.Ground.Width / 2.0, geometry.Ground.Height / 2.0);
        dc.Pop();

        var contactOpacity = Math.Clamp(ContactShadowSlider.Value / 100.0 * 0.82, 0, 0.86);
        if (contactOpacity <= 0)
            return;

        var contactBrush = CreateSoftShadowBrush(contactOpacity, 0.28);
        DrawEllipseRect(dc, geometry.LeftContact, contactBrush);
        DrawEllipseRect(dc, geometry.RightContact, contactBrush);
    }

    private static void DrawEllipseRect(DrawingContext dc, Rect rect, Brush brush)
    {
        var center = new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
        dc.DrawEllipse(brush, null, center, rect.Width / 2.0, rect.Height / 2.0);
    }

    private static RadialGradientBrush CreateSoftShadowBrush(double opacity, double softness)
    {
        var alpha = (byte)Math.Clamp((int)Math.Round(255 * opacity), 0, 255);
        var middleAlpha = (byte)Math.Clamp((int)Math.Round(alpha * 0.72), 0, 255);
        var softStart = Math.Clamp(0.38 + softness * 0.22, 0.38, 0.60);
        var softMiddle = Math.Clamp(0.72 + softness * 0.12, 0.72, 0.84);

        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 0, 0, 0), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 0, 0, 0), softStart));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(middleAlpha, 0, 0, 0), softMiddle));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
        brush.Freeze();
        return brush;
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

    private readonly record struct ShadowGeometry(Rect Ground, Rect LeftContact, Rect RightContact, double AngleDegrees);
    private readonly record struct TireContactRatios(double LeftX, double RightX, double LeftY, double RightY);

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
        public bool ShadowEnabled { get; set; } = true;
        public double ShadowOpacity { get; set; } = 34;
        public double ShadowSoftness { get; set; } = 20;
        public double ShadowWidth { get; set; } = 88;
        public double ShadowLength { get; set; } = 44;
        public double ShadowAngle { get; set; }
        public double ShadowX { get; set; }
        public double ShadowY { get; set; }
        public double ContactShadow { get; set; } = 62;
        public DateTime SavedAt { get; set; }
    }
}
