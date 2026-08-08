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

    private void ComposerWindow_Phase7ReflectionLoaded(object sender, RoutedEventArgs e)
    {
        ComposerWindow_Phase7Loaded(sender, e);

        if (!ReflectionControlsReady())
            return;

        _restoringReflectionState = true;
        ReflectionModeCombo.ItemsSource = new[] { "Glass", "Water" };
        ReflectionModeCombo.SelectedItem = "Glass";
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

        ApplyReflectionPreset(ReflectionModeCombo.SelectedItem.ToString() ?? "Glass", showStatus: true);
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

        var mode = ReflectionModeCombo.SelectedItem?.ToString() ?? "Glass";
        ApplyReflectionPreset(mode, showStatus: true);
    }

    private void ResetReflection_Click(object sender, RoutedEventArgs e)
    {
        if (!ReflectionControlsReady())
            return;

        _restoringReflectionState = true;
        ReflectionEnabledCheckBox.IsChecked = false;
        ReflectionModeCombo.SelectedItem = "Glass";
        ReflectionOpacitySlider.Value = 24;
        ReflectionLengthSlider.Value = 62;
        ReflectionBlurSlider.Value = 1.4;
        ReflectionFadeSlider.Value = 84;
        ReflectionOffsetSlider.Value = 0;
        ReflectionRippleSlider.Value = 0;
        _restoringReflectionState = false;

        UpdateReflectionValueLabels();
        UpdateReflectionPreview();
        SaveReflectionState();
        ComposerStatusText.Text = "Reflection reset.";
    }

    private void ApplyReflectionPreset(string mode, bool showStatus)
    {
        if (!ReflectionControlsReady())
            return;

        _restoringReflectionState = true;
        ReflectionEnabledCheckBox.IsChecked = true;
        ReflectionModeCombo.SelectedItem = mode;

        if (mode.Equals("Water", StringComparison.OrdinalIgnoreCase))
        {
            ReflectionOpacitySlider.Value = 21;
            ReflectionLengthSlider.Value = 64;
            ReflectionBlurSlider.Value = 3.2;
            ReflectionFadeSlider.Value = 90;
            ReflectionOffsetSlider.Value = 1;
            ReflectionRippleSlider.Value = 6;
        }
        else
        {
            ReflectionOpacitySlider.Value = 26;
            ReflectionLengthSlider.Value = 68;
            ReflectionBlurSlider.Value = 1.2;
            ReflectionFadeSlider.Value = 84;
            ReflectionOffsetSlider.Value = 0;
            ReflectionRippleSlider.Value = 0;
        }

        _restoringReflectionState = false;
        UpdateReflectionValueLabels();
        UpdateReflectionPreview();
        SaveReflectionState();

        if (showStatus)
        {
            ComposerStatusText.Text = $"Photorealistic {mode.ToLowerInvariant()} reflection created.";
            ComposerDetailText.Text = mode.Equals("Water", StringComparison.OrdinalIgnoreCase)
                ? "Anchored at the tire contact line with compressed perspective, soft fade and subtle water ripples."
                : "Anchored at the tire contact line with a short, tapered studio-floor reflection.";
        }
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

        var mode = ReflectionModeCombo.SelectedItem?.ToString() ?? "Glass";
        var sourceDepth = mode.Equals("Water", StringComparison.OrdinalIgnoreCase) ? 0.46 : 0.42;
        var contactRatio = FindGroundContactRatio(_vehicleBitmap);

        _reflectionBitmap = BuildPhotorealisticReflectionBitmap(
            _vehicleBitmap,
            mode,
            ReflectionFadeSlider.Value,
            ReflectionRippleSlider.Value,
            ReflectionBlurSlider.Value,
            sourceDepth);

        var naturalSourceHeight = vehicle.Height * sourceDepth;
        var reflectionHeight = naturalSourceHeight * ReflectionLengthSlider.Value / 100.0;
        var groundY = vehicle.Top + vehicle.Height * contactRatio + ReflectionOffsetSlider.Value;

        ReflectionPreviewImage.Source = _reflectionBitmap;
        ReflectionPreviewImage.Opacity = ReflectionOpacitySlider.Value / 100.0;
        ReflectionPreviewImage.Width = vehicle.Width;
        ReflectionPreviewImage.Height = Math.Max(2, reflectionHeight);
        Canvas.SetLeft(ReflectionPreviewImage, vehicle.Left);
        Canvas.SetTop(ReflectionPreviewImage, groundY);

        // A floor reflection is aligned to the ground plane. It must not inherit
        // the car layer's 2-D rotation or it appears to swing away from the tires.
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

        // Ignore isolated antialias pixels and look for the lowest meaningful row.
        var minimumPixels = Math.Max(3, width / 300);
        for (var y = height - 1; y >= 0; y--)
        {
            var count = 0;
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                if (pixels[row + x * 4 + 3] >= 48 && ++count >= minimumPixels)
                    return Math.Clamp(y / (double)Math.Max(1, height - 1), 0.82, 0.995);
            }
        }

        return 0.96;
    }

    private static BitmapSource BuildPhotorealisticReflectionBitmap(
        BitmapSource source,
        string mode,
        double fadePercent,
        double rippleStrength,
        double blurRadius,
        double sourceDepthFraction)
    {
        var bgra = EnsureBgra32(source);
        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var input = new byte[stride * height];
        bgra.CopyPixels(input, stride, 0);

        var contactY = Math.Clamp((int)Math.Round(FindGroundContactRatio(bgra) * (height - 1)), 0, height - 1);
        var sourceDepth = Math.Clamp((int)Math.Round(height * sourceDepthFraction), 8, Math.Max(8, height));
        var outputHeight = sourceDepth;
        var outputStride = width * 4;
        var output = new byte[outputStride * outputHeight];

        var isWater = mode.Equals("Water", StringComparison.OrdinalIgnoreCase);
        var fadeStrength = Math.Clamp(fadePercent / 100.0, 0.10, 1.0);
        var fadePower = 1.45 + fadeStrength * 2.5;
        var ripple = isWater ? Math.Clamp(rippleStrength, 0, 20) : 0;
        var perspectiveTaper = isWater ? 0.13 : 0.085;

        for (var y = 0; y < outputHeight; y++)
        {
            var progress = outputHeight <= 1 ? 0 : y / (double)(outputHeight - 1);
            var sourceY = Math.Clamp(contactY - y, 0, height - 1);
            var fade = Math.Pow(Math.Max(0, 1.0 - progress), fadePower);

            // Distant parts of a floor reflection converge slightly toward center.
            var rowScale = 1.0 - perspectiveTaper * progress;
            var visibleWidth = Math.Max(1, width * rowScale);
            var inset = (width - visibleWidth) / 2.0;
            var rippleShift = ripple <= 0
                ? 0
                : Math.Sin(y * 0.16) * ripple * (0.18 + progress * 0.82);

            var sourceRow = sourceY * stride;
            var destinationRow = y * outputStride;

            for (var x = 0; x < width; x++)
            {
                var normalized = (x - inset) / visibleWidth;
                if (normalized < 0 || normalized > 1)
                    continue;

                var sourceX = (int)Math.Round(normalized * (width - 1) + rippleShift);
                sourceX = Math.Clamp(sourceX, 0, width - 1);

                var src = sourceRow + sourceX * 4;
                var dst = destinationRow + x * 4;
                var alpha = input[src + 3];
                if (alpha <= 2)
                    continue;

                // Reflections are normally a little darker and less saturated than
                // the source object, especially on wet or glossy real-world floors.
                var b = input[src];
                var g = input[src + 1];
                var r = input[src + 2];
                var lum = 0.114 * b + 0.587 * g + 0.299 * r;
                var saturation = isWater ? 0.72 : 0.84;
                var brightness = isWater ? 0.82 : 0.90;

                output[dst] = ClampByte((lum + (b - lum) * saturation) * brightness);
                output[dst + 1] = ClampByte((lum + (g - lum) * saturation) * brightness);
                output[dst + 2] = ClampByte((lum + (r - lum) * saturation) * brightness);
                output[dst + 3] = (byte)Math.Clamp((int)Math.Round(alpha * fade), 0, 255);
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

        var mode = ReflectionModeCombo.SelectedItem?.ToString() ?? "Glass";
        var sourceDepth = mode.Equals("Water", StringComparison.OrdinalIgnoreCase) ? 0.46 : 0.42;
        var reflection = BuildPhotorealisticReflectionBitmap(
            _vehicleBitmap,
            mode,
            ReflectionFadeSlider.Value,
            ReflectionRippleSlider.Value,
            ReflectionBlurSlider.Value,
            sourceDepth);

        var contactRatio = FindGroundContactRatio(_vehicleBitmap);
        var naturalSourceHeight = vehicle.Height * sourceDepth;
        var height = naturalSourceHeight * ReflectionLengthSlider.Value / 100.0;
        var groundY = vehicle.Top + vehicle.Height * contactRatio + ReflectionOffsetSlider.Value * scaleFactor;

        var rect = new Rect(vehicle.Left, groundY, vehicle.Width, Math.Max(2, height));

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
                Mode = ReflectionModeCombo.SelectedItem?.ToString() ?? "Glass",
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
            // Reflection state should never block the Photo Studio workflow.
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
            ReflectionModeCombo.SelectedItem = state.Mode is "Water" ? "Water" : "Glass";
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
        public string Mode { get; set; } = "Glass";
        public double Opacity { get; set; } = 26;
        public double Length { get; set; } = 68;
        public double Blur { get; set; } = 1.2;
        public double Fade { get; set; } = 84;
        public double Offset { get; set; }
        public double Ripple { get; set; }
        public DateTime SavedAt { get; set; }
    }
}
