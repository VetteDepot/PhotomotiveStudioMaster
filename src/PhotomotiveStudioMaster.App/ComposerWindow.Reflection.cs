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

    private void ComposerWindow_Phase7ReflectionLoaded(object sender, RoutedEventArgs e)
    {
        ComposerWindow_Phase7Loaded(sender, e);

        _restoringReflectionState = true;
        ReflectionModeCombo.ItemsSource = new[] { "Glass", "Water" };
        ReflectionModeCombo.SelectedItem = "Glass";
        ReflectionEnabledCheckBox.IsChecked = false;
        _restoringReflectionState = false;

        TryRestoreReflectionState();
        UpdateReflectionValueLabels();
        UpdateReflectionPreview();
    }

    private void ReflectionControl_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _restoringReflectionState)
            return;

        UpdateReflectionPreview();
        SaveReflectionState();
    }

    private void ReflectionMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _restoringReflectionState || ReflectionModeCombo.SelectedItem is null)
            return;

        ApplyReflectionPreset(ReflectionModeCombo.SelectedItem.ToString() ?? "Glass", showStatus: true);
    }

    private void ReflectionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateReflectionValueLabels();
        if (!_initialized || _restoringReflectionState)
            return;

        UpdateReflectionPreview();
        SaveReflectionState();
    }

    private void AutoReflection_Click(object sender, RoutedEventArgs e)
    {
        var mode = ReflectionModeCombo.SelectedItem?.ToString() ?? "Glass";
        ApplyReflectionPreset(mode, showStatus: true);
    }

    private void ResetReflection_Click(object sender, RoutedEventArgs e)
    {
        _restoringReflectionState = true;
        ReflectionEnabledCheckBox.IsChecked = false;
        ReflectionModeCombo.SelectedItem = "Glass";
        ReflectionOpacitySlider.Value = 30;
        ReflectionLengthSlider.Value = 65;
        ReflectionBlurSlider.Value = 1;
        ReflectionFadeSlider.Value = 78;
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
        _restoringReflectionState = true;
        ReflectionEnabledCheckBox.IsChecked = true;
        ReflectionModeCombo.SelectedItem = mode;

        if (mode.Equals("Water", StringComparison.OrdinalIgnoreCase))
        {
            ReflectionOpacitySlider.Value = 25;
            ReflectionLengthSlider.Value = 58;
            ReflectionBlurSlider.Value = 3;
            ReflectionFadeSlider.Value = 88;
            ReflectionOffsetSlider.Value = 2;
            ReflectionRippleSlider.Value = 7;
        }
        else
        {
            ReflectionOpacitySlider.Value = 32;
            ReflectionLengthSlider.Value = 72;
            ReflectionBlurSlider.Value = 1;
            ReflectionFadeSlider.Value = 78;
            ReflectionOffsetSlider.Value = 0;
            ReflectionRippleSlider.Value = 0;
        }

        _restoringReflectionState = false;
        UpdateReflectionValueLabels();
        UpdateReflectionPreview();
        SaveReflectionState();

        if (showStatus)
        {
            ComposerStatusText.Text = $"{mode} reflection created.";
            ComposerDetailText.Text = mode.Equals("Water", StringComparison.OrdinalIgnoreCase)
                ? "Water mode adds a softer, shorter reflection with subtle horizontal ripples."
                : "Glass mode creates a clean mirrored reflection directly below the vehicle.";
        }
    }

    private void UpdateReflectionValueLabels()
    {
        if (ReflectionOpacityValueText is null)
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
        if (ReflectionCanvas is null || ReflectionPreviewImage is null)
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
        _reflectionBitmap = BuildReflectionBitmap(
            _vehicleBitmap,
            mode,
            ReflectionFadeSlider.Value,
            ReflectionRippleSlider.Value,
            ReflectionBlurSlider.Value);

        ReflectionPreviewImage.Source = _reflectionBitmap;
        ReflectionPreviewImage.Opacity = ReflectionOpacitySlider.Value / 100.0;
        ReflectionPreviewImage.Width = vehicle.Width;
        ReflectionPreviewImage.Height = vehicle.Height * ReflectionLengthSlider.Value / 100.0;
        Canvas.SetLeft(ReflectionPreviewImage, vehicle.Left);
        Canvas.SetTop(ReflectionPreviewImage, vehicle.Bottom + ReflectionOffsetSlider.Value);
        ReflectionPreviewImage.RenderTransformOrigin = new Point(0.5, 0.0);
        ReflectionPreviewImage.RenderTransform = new RotateTransform(-RotationSlider.Value);
    }

    private static BitmapSource BuildReflectionBitmap(
        BitmapSource source,
        string mode,
        double fadePercent,
        double rippleStrength,
        double blurRadius)
    {
        var bgra = EnsureBgra32(source);
        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var input = new byte[stride * height];
        var output = new byte[stride * height];
        bgra.CopyPixels(input, stride, 0);

        var isWater = mode.Equals("Water", StringComparison.OrdinalIgnoreCase);
        var fadePower = 0.65 + Math.Clamp(fadePercent / 100.0, 0, 1) * 2.2;
        var ripple = isWater ? Math.Clamp(rippleStrength, 0, 20) : 0;

        for (var y = 0; y < height; y++)
        {
            var sourceY = height - 1 - y;
            var progress = height <= 1 ? 1.0 : y / (double)(height - 1);
            var fade = Math.Pow(Math.Max(0, 1.0 - progress), fadePower);
            var shift = ripple <= 0
                ? 0
                : (int)Math.Round(Math.Sin(y * 0.105) * ripple * (0.25 + progress * 0.75));

            var sourceRow = sourceY * stride;
            var destinationRow = y * stride;
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Clamp(x + shift, 0, width - 1);
                var src = sourceRow + sourceX * 4;
                var dst = destinationRow + x * 4;

                output[dst] = input[src];
                output[dst + 1] = input[src + 1];
                output[dst + 2] = input[src + 2];
                output[dst + 3] = (byte)Math.Clamp((int)Math.Round(input[src + 3] * fade), 0, 255);
            }
        }

        var reflected = BitmapSource.Create(
            width,
            height,
            bgra.DpiX,
            bgra.DpiY,
            PixelFormats.Bgra32,
            null,
            output,
            stride);
        reflected.Freeze();

        if (blurRadius > 0.05)
            reflected = BlurBitmap(reflected, Math.Clamp(blurRadius, 0, 8));

        return reflected;
    }

    private void DrawExportReflection(DrawingContext dc, Rect vehicle, double scaleFactor)
    {
        if (ReflectionEnabledCheckBox.IsChecked != true || _vehicleBitmap is null)
            return;

        var mode = ReflectionModeCombo.SelectedItem?.ToString() ?? "Glass";
        var reflection = BuildReflectionBitmap(
            _vehicleBitmap,
            mode,
            ReflectionFadeSlider.Value,
            ReflectionRippleSlider.Value,
            ReflectionBlurSlider.Value);

        var height = vehicle.Height * ReflectionLengthSlider.Value / 100.0;
        var rect = new Rect(
            vehicle.Left,
            vehicle.Bottom + ReflectionOffsetSlider.Value * scaleFactor,
            vehicle.Width,
            height);

        var centerX = rect.Left + rect.Width / 2.0;
        var pivotY = rect.Top;
        dc.PushOpacity(Math.Clamp(ReflectionOpacitySlider.Value / 100.0, 0, 1));
        dc.PushTransform(new RotateTransform(-RotationSlider.Value, centerX, pivotY));
        dc.DrawImage(reflection, rect);
        dc.Pop();
        dc.Pop();
    }

    private void SaveReflectionState()
    {
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
        public double Opacity { get; set; } = 32;
        public double Length { get; set; } = 72;
        public double Blur { get; set; } = 1;
        public double Fade { get; set; } = 78;
        public double Offset { get; set; }
        public double Ripple { get; set; }
        public DateTime SavedAt { get; set; }
    }
}
