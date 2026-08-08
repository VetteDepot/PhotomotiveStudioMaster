using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotomotiveStudioMaster.App;

public partial class ComposerWindow
{
    private bool _restoringReflectionState;
    private BitmapSource? _reflectionBitmap;
    private bool _reflectionPlacementHandlersAttached;

    private static readonly string[] ReflectionSurfaces =
    {
        "Studio Gloss",
        "Polished Concrete",
        "Wet Asphalt",
        "Marble",
        "Black Acrylic",
        "Water",
        "Ice",
        "Salt Flats"
    };

    private void ComposerWindow_Phase7ReflectionLoaded(object sender, RoutedEventArgs e)
    {
        ComposerWindow_Phase7Loaded(sender, e);

        if (!ReflectionControlsReady())
            return;

        _restoringReflectionState = true;
        ReflectionModeCombo.ItemsSource = ReflectionSurfaces;
        ReflectionModeCombo.SelectedItem = "Studio Gloss";
        ReflectionEnabledCheckBox.IsChecked = false;
        _restoringReflectionState = false;

        AttachReflectionPlacementHandlers();
        TryRestoreReflectionState();
        UpdateReflectionValueLabels();
        UpdateReflectionPreview();
    }

    private bool ReflectionControlsReady()
    {
        return ReflectionEnabledCheckBox is not null &&
               ReflectionModeCombo is not null &&
               ReflectionOpacityValueText is not null &&
               ReflectionLengthValueText is not null &&
               ReflectionBlurValueText is not null &&
               ReflectionFadeValueText is not null &&
               ReflectionOffsetValueText is not null &&
               ReflectionRippleValueText is not null &&
               ReflectionOpacitySlider is not null &&
               ReflectionLengthSlider is not null &&
               ReflectionBlurSlider is not null &&
               ReflectionFadeSlider is not null &&
               ReflectionOffsetSlider is not null &&
               ReflectionRippleSlider is not null &&
               ReflectionCanvas is not null &&
               ReflectionPreviewImage is not null;
    }

    private void AttachReflectionPlacementHandlers()
    {
        if (_reflectionPlacementHandlersAttached)
            return;

        _reflectionPlacementHandlersAttached = true;
        ScaleSlider.ValueChanged += ReflectionPlacementChanged;
        XSlider.ValueChanged += ReflectionPlacementChanged;
        YSlider.ValueChanged += ReflectionPlacementChanged;
        RotationSlider.ValueChanged += ReflectionPlacementChanged;

        RealismBrightnessSlider.ValueChanged += ReflectionVehicleAppearanceChanged;
        RealismContrastSlider.ValueChanged += ReflectionVehicleAppearanceChanged;
        RealismTemperatureSlider.ValueChanged += ReflectionVehicleAppearanceChanged;
        RealismTintSlider.ValueChanged += ReflectionVehicleAppearanceChanged;
        RealismSaturationSlider.ValueChanged += ReflectionVehicleAppearanceChanged;
        GroundBounceSlider.ValueChanged += ReflectionVehicleAppearanceChanged;
        SkyFillSlider.ValueChanged += ReflectionVehicleAppearanceChanged;
        DofMatchSlider.ValueChanged += ReflectionVehicleAppearanceChanged;
        NoiseMatchSlider.ValueChanged += ReflectionVehicleAppearanceChanged;
    }

    private void ReflectionPlacementChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initialized && !_restoringReflectionState)
            UpdateReflectionPreview();
    }

    private void ReflectionVehicleAppearanceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _restoringReflectionState)
            return;

        Dispatcher.BeginInvoke(new Action(UpdateReflectionPreview), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ReflectionControl_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _restoringReflectionState || !ReflectionControlsReady())
            return;

        UpdateReflectionPreview();
        SaveReflectionState();
    }

    private void ReflectionMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _restoringReflectionState || !ReflectionControlsReady() || ReflectionModeCombo.SelectedItem is null)
            return;

        ApplyReflectionPreset(ReflectionModeCombo.SelectedItem.ToString() ?? "Studio Gloss", showStatus: true);
    }

    private void ReflectionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ReflectionControlsReady())
            return;

        UpdateReflectionValueLabels();
        if (!_initialized || _restoringReflectionState)
            return;

        UpdateReflectionPreview();
        SaveReflectionState();
    }

    private void AutoReflection_Click(object sender, RoutedEventArgs e)
    {
        if (!ReflectionControlsReady())
            return;

        var surface = ReflectionModeCombo.SelectedItem?.ToString() ?? "Studio Gloss";
        ApplyReflectionPreset(surface, showStatus: true);
    }

    private void ResetReflection_Click(object sender, RoutedEventArgs e)
    {
        if (!ReflectionControlsReady())
            return;

        _restoringReflectionState = true;
        ReflectionEnabledCheckBox.IsChecked = false;
        ReflectionModeCombo.SelectedItem = "Studio Gloss";
        ReflectionOpacitySlider.Value = 22;
        ReflectionLengthSlider.Value = 58;
        ReflectionBlurSlider.Value = 1.2;
        ReflectionFadeSlider.Value = 88;
        ReflectionOffsetSlider.Value = 0;
        ReflectionRippleSlider.Value = 0;
        _restoringReflectionState = false;

        UpdateReflectionValueLabels();
        UpdateReflectionPreview();
        SaveReflectionState();
        ComposerStatusText.Text = "Surface reflection reset.";
    }

    private void ApplyReflectionPreset(string surface, bool showStatus)
    {
        if (!ReflectionControlsReady())
            return;

        var profile = GetSurfaceProfile(surface);

        _restoringReflectionState = true;
        ReflectionEnabledCheckBox.IsChecked = true;
        ReflectionModeCombo.SelectedItem = profile.Name;
        ReflectionOpacitySlider.Value = profile.Opacity;
        ReflectionLengthSlider.Value = profile.Length;
        ReflectionBlurSlider.Value = profile.Blur;
        ReflectionFadeSlider.Value = profile.Fade;
        ReflectionOffsetSlider.Value = profile.Offset;
        ReflectionRippleSlider.Value = profile.Ripple;
        _restoringReflectionState = false;

        UpdateReflectionValueLabels();
        UpdateReflectionPreview();
        SaveReflectionState();

        if (showStatus)
        {
            ComposerStatusText.Text = $"{profile.Name} surface reflection created.";
            ComposerDetailText.Text = profile.Description;
        }
    }

    private static SurfaceProfile GetSurfaceProfile(string? surface)
    {
        return surface switch
        {
            "Polished Concrete" => new SurfaceProfile("Polished Concrete", 15, 46, 2.8, 92, 0, 0, 0.33, 0.16, 0.79, 0.80, 0.05,
                "Soft, short reflection with subdued color and a matte-polished floor response."),
            "Wet Asphalt" => new SurfaceProfile("Wet Asphalt", 19, 55, 4.2, 91, 1, 4, 0.38, 0.20, 0.74, 0.76, 0.07,
                "Dark, blurred reflection with subtle pavement distortion and rapid falloff."),
            "Marble" => new SurfaceProfile("Marble", 24, 61, 1.8, 86, 0, 0, 0.37, 0.13, 0.84, 0.89, 0.10,
                "Clean luxury-floor reflection with moderate compression and gentle highlight retention."),
            "Black Acrylic" => new SurfaceProfile("Black Acrylic", 38, 70, 0.8, 80, 0, 0, 0.40, 0.09, 0.90, 0.94, 0.16,
                "Deep, crisp premium reflection with stronger chrome and highlight response."),
            "Water" => new SurfaceProfile("Water", 20, 58, 3.6, 91, 1, 7, 0.40, 0.22, 0.73, 0.78, 0.06,
                "Soft water reflection with horizontal ripple distortion and fast tonal falloff."),
            "Ice" => new SurfaceProfile("Ice", 25, 65, 2.0, 86, 0, 2, 0.39, 0.12, 0.80, 0.91, 0.12,
                "Cool, semi-crisp reflection with restrained distortion and bright highlight response."),
            "Salt Flats" => new SurfaceProfile("Salt Flats", 10, 35, 4.5, 96, 0, 0, 0.28, 0.22, 0.70, 0.74, 0.02,
                "Very faint, diffuse ground response for dry bright surfaces."),
            _ => new SurfaceProfile("Studio Gloss", 24, 56, 1.1, 88, 0, 0, 0.36, 0.12, 0.84, 0.91, 0.12,
                "Tight lower-body reflection anchored to the tires with premium studio-floor compression and chrome detail."),
        };
    }

    private void UpdateReflectionValueLabels()
    {
        if (!ReflectionControlsReady())
            return;

        ReflectionOpacityValueText.Text = $"{ReflectionOpacitySlider.Value:0}%";
        ReflectionLengthValueText.Text = $"{ReflectionLengthSlider.Value:0}%";
        ReflectionBlurValueText.Text = $"{ReflectionBlurSlider.Value:0.0}px";
        ReflectionFadeValueText.Text = $"{ReflectionFadeSlider.Value:0}%";
        ReflectionOffsetValueText.Text = $"{ReflectionOffsetSlider.Value:+0;-0;0}";
        ReflectionRippleValueText.Text = $"{ReflectionRippleSlider.Value:0}";
    }

    private void UpdateReflectionPreview()
    {
        if (!ReflectionControlsReady())
            return;

        var enabled = ReflectionEnabledCheckBox.IsChecked == true && _vehicleBitmap is not null;
        ReflectionCanvas.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ReflectionBadge.Visibility = enabled && !_showingOriginal ? Visibility.Visible : Visibility.Collapsed;

        if (!enabled || _vehicleBitmap is null)
            return;

        var vehicle = GetVehicleRect(1.0);
        if (vehicle.IsEmpty)
            return;

        var profile = GetSurfaceProfile(ReflectionModeCombo.SelectedItem?.ToString());
        var contactRatio = FindGroundContactRatio(_vehicleBitmap);

        _reflectionBitmap = BuildPhotorealisticReflectionBitmap(
            _vehicleBitmap,
            profile,
            ReflectionFadeSlider.Value,
            ReflectionRippleSlider.Value,
            ReflectionBlurSlider.Value);

        var naturalSourceHeight = vehicle.Height * profile.SourceDepth;
        var reflectionHeight = naturalSourceHeight * ReflectionLengthSlider.Value / 100.0;
        var groundY = vehicle.Top + vehicle.Height * contactRatio + ReflectionOffsetSlider.Value;

        ReflectionPreviewImage.Source = _reflectionBitmap;
        ReflectionPreviewImage.Opacity = ReflectionOpacitySlider.Value / 100.0;
        ReflectionPreviewImage.Width = vehicle.Width;
        ReflectionPreviewImage.Height = Math.Max(2, reflectionHeight);
        Canvas.SetLeft(ReflectionPreviewImage, vehicle.Left);
        Canvas.SetTop(ReflectionPreviewImage, groundY - 0.5);
        ReflectionPreviewImage.RenderTransform = Transform.Identity;
    }

    private static double FindGroundContactRatio(BitmapSource source)
    {
        var bgra = EnsureBgra32(source);
        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        if (width <= 0 || height <= 0)
            return 0.96;

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        var minimumPixels = Math.Max(4, width / 220);
        for (var y = height - 1; y >= 0; y--)
        {
            var count = 0;
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                if (pixels[row + x * 4 + 3] >= 64 && ++count >= minimumPixels)
                    return Math.Clamp(y / (double)Math.Max(1, height - 1), 0.84, 0.995);
            }
        }

        return 0.96;
    }

    private static BitmapSource BuildPhotorealisticReflectionBitmap(
        BitmapSource source,
        SurfaceProfile profile,
        double fadePercent,
        double rippleStrength,
        double blurRadius)
    {
        var bgra = EnsureBgra32(source);
        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var input = new byte[stride * height];
        bgra.CopyPixels(input, stride, 0);

        var contactY = Math.Clamp((int)Math.Round(FindGroundContactRatio(bgra) * (height - 1)), 0, height - 1);
        var sourceDepth = Math.Clamp((int)Math.Round(height * profile.SourceDepth), 8, Math.Max(8, height));
        var outputHeight = sourceDepth;
        var outputStride = width * 4;
        var output = new byte[outputStride * outputHeight];

        var fadeStrength = Math.Clamp(fadePercent / 100.0, 0.10, 1.0);
        var fadePower = 1.75 + fadeStrength * 3.0;
        var ripple = Math.Clamp(rippleStrength, 0, 20);

        for (var y = 0; y < outputHeight; y++)
        {
            var progress = outputHeight <= 1 ? 0 : y / (double)(outputHeight - 1);

            // Non-linear vertical sampling compresses the reflection toward the
            // contact line, which is how a real horizontal floor reflection reads.
            var compressedProgress = Math.Pow(progress, 0.72);
            var sourceOffset = (int)Math.Round(compressedProgress * (sourceDepth - 1));
            var sourceY = Math.Clamp(contactY - sourceOffset, 0, height - 1);
            var fade = Math.Pow(Math.Max(0, 1.0 - progress), fadePower);

            var rowScale = 1.0 - profile.PerspectiveTaper * Math.Pow(progress, 0.85);
            var visibleWidth = Math.Max(1, width * rowScale);
            var inset = (width - visibleWidth) / 2.0;
            var rippleShift = ripple <= 0
                ? 0
                : Math.Sin(y * 0.17) * ripple * (0.15 + progress * 0.85);

            var sourceRow = sourceY * stride;
            var destinationRow = y * outputStride;

            for (var x = 0; x < width; x++)
            {
                var normalized = (x - inset) / visibleWidth;
                if (normalized < 0 || normalized > 1)
                    continue;

                var sourceX = Math.Clamp((int)Math.Round(normalized * (width - 1) + rippleShift), 0, width - 1);
                var src = sourceRow + sourceX * 4;
                var dst = destinationRow + x * 4;
                var alpha = input[src + 3];
                if (alpha <= 3)
                    continue;

                var b = input[src];
                var g = input[src + 1];
                var r = input[src + 2];
                var lum = 0.114 * b + 0.587 * g + 0.299 * r;

                var saturation = profile.Saturation;
                var brightness = profile.Brightness;

                // Chrome, bright trim and specular highlights remain a little more
                // visible in polished-surface reflections than midtone body paint.
                var highlight = Math.Clamp((lum - 175.0) / 80.0, 0, 1);
                var highlightBoost = 1.0 + profile.HighlightBoost * highlight;

                output[dst] = ClampByte((lum + (b - lum) * saturation) * brightness * highlightBoost);
                output[dst + 1] = ClampByte((lum + (g - lum) * saturation) * brightness * highlightBoost);
                output[dst + 2] = ClampByte((lum + (r - lum) * saturation) * brightness * highlightBoost);

                // Strengthen the first few rows so the reflection visually touches
                // the tires/rocker panels, then fade rapidly into the floor.
                var contactBoost = progress < 0.08 ? 1.10 - progress * 1.25 : 1.0;
                output[dst + 3] = (byte)Math.Clamp((int)Math.Round(alpha * fade * contactBoost), 0, 255);
            }
        }

        var reflected = BitmapSource.Create(
            width,
            outputHeight,
            bgra.DpiX,
            bgra.DpiY,
            PixelFormats.Bgra32,
            null,
            output,
            outputStride);
        reflected.Freeze();

        if (blurRadius > 0.05)
            reflected = BlurBitmap(reflected, Math.Clamp(blurRadius, 0, 8));

        return reflected;
    }

    private void DrawExportReflection(DrawingContext dc, Rect vehicle, double scaleFactor)
    {
        if (ReflectionEnabledCheckBox.IsChecked != true || _vehicleBitmap is null || !ReflectionControlsReady())
            return;

        var profile = GetSurfaceProfile(ReflectionModeCombo.SelectedItem?.ToString());
        var reflection = BuildPhotorealisticReflectionBitmap(
            _vehicleBitmap,
            profile,
            ReflectionFadeSlider.Value,
            ReflectionRippleSlider.Value,
            ReflectionBlurSlider.Value);

        var contactRatio = FindGroundContactRatio(_vehicleBitmap);
        var naturalSourceHeight = vehicle.Height * profile.SourceDepth;
        var height = naturalSourceHeight * ReflectionLengthSlider.Value / 100.0;
        var groundY = vehicle.Top + vehicle.Height * contactRatio + ReflectionOffsetSlider.Value * scaleFactor;
        var rect = new Rect(vehicle.Left, groundY - 0.5 * scaleFactor, vehicle.Width, Math.Max(2, height));

        dc.PushOpacity(Math.Clamp(ReflectionOpacitySlider.Value / 100.0, 0, 1));
        dc.DrawImage(reflection, rect);
        dc.Pop();
    }

    private void SaveReflectionState()
    {
        if (!ReflectionControlsReady())
            return;

        try
        {
            var state = new ReflectionState
            {
                Enabled = ReflectionEnabledCheckBox.IsChecked == true,
                Mode = ReflectionModeCombo.SelectedItem?.ToString() ?? "Studio Gloss",
                Opacity = ReflectionOpacitySlider.Value,
                Length = ReflectionLengthSlider.Value,
                Blur = ReflectionBlurSlider.Value,
                Fade = ReflectionFadeSlider.Value,
                Offset = ReflectionOffsetSlider.Value,
                Ripple = ReflectionRippleSlider.Value,
                SavedAt = DateTime.Now
            };

            var path = Path.Combine(GetProjectFolder(), _job.JobNumber + ".reflection.json");
            File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private void TryRestoreReflectionState()
    {
        if (!ReflectionControlsReady())
            return;

        var path = Path.Combine(GetProjectFolder(), _job.JobNumber + ".reflection.json");
        if (!File.Exists(path))
        {
            ReflectionEnabledCheckBox.IsChecked = false;
            return;
        }

        try
        {
            var state = JsonSerializer.Deserialize<ReflectionState>(File.ReadAllText(path));
            if (state is null)
                return;

            _restoringReflectionState = true;
            ReflectionEnabledCheckBox.IsChecked = state.Enabled;
            ReflectionModeCombo.SelectedItem = ReflectionSurfaces.Contains(state.Mode) ? state.Mode : "Studio Gloss";
            ReflectionOpacitySlider.Value = Math.Clamp(state.Opacity, ReflectionOpacitySlider.Minimum, ReflectionOpacitySlider.Maximum);
            ReflectionLengthSlider.Value = Math.Clamp(state.Length, ReflectionLengthSlider.Minimum, ReflectionLengthSlider.Maximum);
            ReflectionBlurSlider.Value = Math.Clamp(state.Blur, ReflectionBlurSlider.Minimum, ReflectionBlurSlider.Maximum);
            ReflectionFadeSlider.Value = Math.Clamp(state.Fade, ReflectionFadeSlider.Minimum, ReflectionFadeSlider.Maximum);
            ReflectionOffsetSlider.Value = Math.Clamp(state.Offset, ReflectionOffsetSlider.Minimum, ReflectionOffsetSlider.Maximum);
            ReflectionRippleSlider.Value = Math.Clamp(state.Ripple, ReflectionRippleSlider.Minimum, ReflectionRippleSlider.Maximum);
        }
        catch
        {
            ReflectionEnabledCheckBox.IsChecked = false;
        }
        finally
        {
            _restoringReflectionState = false;
        }
    }

    private sealed class ReflectionState
    {
        public bool Enabled { get; set; }
        public string Mode { get; set; } = "Studio Gloss";
        public double Opacity { get; set; } = 24;
        public double Length { get; set; } = 56;
        public double Blur { get; set; } = 1.1;
        public double Fade { get; set; } = 88;
        public double Offset { get; set; }
        public double Ripple { get; set; }
        public DateTime SavedAt { get; set; }
    }

    private readonly record struct SurfaceProfile(
        string Name,
        double Opacity,
        double Length,
        double Blur,
        double Fade,
        double Offset,
        double Ripple,
        double SourceDepth,
        double PerspectiveTaper,
        double Saturation,
        double Brightness,
        double HighlightBoost,
        string Description);
}
