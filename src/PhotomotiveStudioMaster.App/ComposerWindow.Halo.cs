using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PhotomotiveStudioMaster.App;

public partial class ComposerWindow
{
    private BitmapSource? _haloBaseBitmap;
    private DispatcherTimer? _haloDebounceTimer;
    private bool _restoringHaloState;

    private void ComposerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_vehicleBitmap is null)
            return;

        // Final output is 3000 px wide. Capping the working vehicle keeps live edge
        // processing responsive without throwing away detail needed by the export.
        _haloBaseBitmap = LimitBitmapDimension(_vehicleBitmap, 3000);
        _vehicleBitmap = _haloBaseBitmap;
        CarPreviewImage.Source = _vehicleBitmap;

        TryRestoreHaloState();
        UpdateHaloValueLabels();
        ApplyHaloAdjustments();
        UpdatePlacementPreview();
        UpdateHaloOverlayVisibility();
    }

    private void HaloSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateHaloValueLabels();
        if (!_initialized || _restoringHaloState || _haloBaseBitmap is null)
            return;

        QueueHaloUpdate();
    }

    private void HaloOverlay_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
            return;

        UpdateHaloOverlayVisibility();
        SaveHaloState();
    }

    private void AutoHaloRepair_Click(object sender, RoutedEventArgs e)
    {
        if (_haloBaseBitmap is null && _vehicleBitmap is not null)
            _haloBaseBitmap = LimitBitmapDimension(_vehicleBitmap, 3000);

        HaloFeatherSlider.Value = 0.5;
        HaloMaskSlider.Value = -1;
        HaloRemovalSlider.Value = 62;
        HaloDecontamSlider.Value = 32;
        HaloDetailSlider.Value = 22;
        HaloOverlayCheckBox.IsChecked = true;

        ApplyHaloAdjustments();
        UpdatePlacementPreview();
        SaveHaloState();
        ComposerStatusText.Text = "Auto Halo Repair applied.";
        ComposerDetailText.Text = "Inspect the red/blue edge overlay, then fine-tune the Halo Inspector sliders if needed.";
    }

    private void ResetHalo_Click(object sender, RoutedEventArgs e)
    {
        HaloFeatherSlider.Value = 0;
        HaloMaskSlider.Value = 0;
        HaloRemovalSlider.Value = 0;
        HaloDecontamSlider.Value = 0;
        HaloDetailSlider.Value = 0;
        HaloOverlayCheckBox.IsChecked = false;

        if (_haloBaseBitmap is not null)
        {
            _vehicleBitmap = _haloBaseBitmap;
            CarPreviewImage.Source = _vehicleBitmap;
            HaloOverlayImage.Source = BuildHaloOverlay(_haloBaseBitmap, _haloBaseBitmap);
            UpdatePlacementPreview();
        }

        UpdateHaloValueLabels();
        UpdateHaloOverlayVisibility();
        SaveHaloState();
        ComposerStatusText.Text = "Halo Inspector reset to the original extraction edge.";
    }

    private void QueueHaloUpdate()
    {
        _haloDebounceTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };

        _haloDebounceTimer.Stop();
        _haloDebounceTimer.Tick -= HaloDebounceTimer_Tick;
        _haloDebounceTimer.Tick += HaloDebounceTimer_Tick;
        _haloDebounceTimer.Start();
    }

    private void HaloDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _haloDebounceTimer?.Stop();
        ApplyHaloAdjustments();
        UpdatePlacementPreview();
        SaveHaloState();
    }

    private void UpdateHaloValueLabels()
    {
        if (HaloFeatherValueText is null)
            return;

        HaloFeatherValueText.Text = $"{HaloFeatherSlider.Value:0.0}px";
        HaloMaskValueText.Text = $"{HaloMaskSlider.Value:+0;-0;0}px";
        HaloRemovalValueText.Text = $"{HaloRemovalSlider.Value:0}%";
        HaloDecontamValueText.Text = $"{HaloDecontamSlider.Value:0}%";
        HaloDetailValueText.Text = $"{HaloDetailSlider.Value:0}%";
    }

    private void UpdateHaloOverlayVisibility()
    {
        var show = HaloOverlayCheckBox.IsChecked == true && !_showingOriginal;
        HaloOverlayImage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        HaloBadge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyHaloAdjustments()
    {
        if (_haloBaseBitmap is null)
            return;

        try
        {
            var source = EnsureBgra32(_haloBaseBitmap);
            var width = source.PixelWidth;
            var height = source.PixelHeight;
            var stride = width * 4;
            var originalPixels = new byte[stride * height];
            source.CopyPixels(originalPixels, stride, 0);
            var pixels = (byte[])originalPixels.Clone();

            var originalAlpha = new byte[width * height];
            for (var y = 0; y < height; y++)
            {
                var row = y * stride;
                var alphaRow = y * width;
                for (var x = 0; x < width; x++)
                    originalAlpha[alphaRow + x] = pixels[row + x * 4 + 3];
            }

            var alpha = (byte[])originalAlpha.Clone();
            var maskShift = (int)Math.Round(HaloMaskSlider.Value);
            if (maskShift != 0)
                alpha = MorphAlpha(alpha, width, height, Math.Abs(maskShift), maskShift > 0);

            var feather = Math.Max(0, HaloFeatherSlider.Value);
            if (feather > 0.01)
            {
                var radius = Math.Max(1, (int)Math.Ceiling(feather));
                var blurred = BoxBlurAlpha(alpha, width, height, radius);
                var blend = Math.Clamp(feather / radius, 0, 1);
                for (var i = 0; i < alpha.Length; i++)
                    alpha[i] = LerpByte(alpha[i], blurred[i], blend);
            }

            var detailRecovery = Math.Clamp(HaloDetailSlider.Value / 100.0, 0, 1);
            if (detailRecovery > 0)
            {
                for (var i = 0; i < alpha.Length; i++)
                    alpha[i] = LerpByte(alpha[i], originalAlpha[i], detailRecovery * 0.55);
            }

            var haloStrength = Math.Clamp(HaloRemovalSlider.Value / 100.0, 0, 1);
            var decontamStrength = Math.Clamp(HaloDecontamSlider.Value / 100.0, 0, 1);

            for (var y = 0; y < height; y++)
            {
                var row = y * stride;
                var alphaRow = y * width;
                for (var x = 0; x < width; x++)
                {
                    var a = alpha[alphaRow + x];
                    var p = row + x * 4;
                    pixels[p + 3] = a;

                    if (a <= 3 || a >= 252 || (haloStrength <= 0 && decontamStrength <= 0))
                        continue;

                    if (!TryGetInteriorColor(originalPixels, originalAlpha, width, height, stride, x, y, out var ib, out var ig, out var ir))
                        continue;

                    var edgeWeight = 0.25 + 0.75 * (1.0 - a / 255.0);
                    var b = pixels[p];
                    var g = pixels[p + 1];
                    var r = pixels[p + 2];

                    if (haloStrength > 0)
                    {
                        var currentLum = Math.Max(1.0, 0.114 * b + 0.587 * g + 0.299 * r);
                        var interiorLum = 0.114 * ib + 0.587 * ig + 0.299 * ir;
                        var ratio = Math.Clamp(interiorLum / currentLum, 0.72, 1.28);
                        var amount = haloStrength * edgeWeight * 0.72;
                        b = ClampByte(b * (1 + (ratio - 1) * amount));
                        g = ClampByte(g * (1 + (ratio - 1) * amount));
                        r = ClampByte(r * (1 + (ratio - 1) * amount));
                    }

                    if (decontamStrength > 0)
                    {
                        var amount = decontamStrength * edgeWeight * 0.68;
                        b = LerpByte(b, ib, amount);
                        g = LerpByte(g, ig, amount);
                        r = LerpByte(r, ir, amount);
                    }

                    pixels[p] = b;
                    pixels[p + 1] = g;
                    pixels[p + 2] = r;
                }
            }

            var processed = BitmapSource.Create(
                width,
                height,
                source.DpiX,
                source.DpiY,
                PixelFormats.Bgra32,
                null,
                pixels,
                stride);
            processed.Freeze();

            _vehicleBitmap = processed;
            CarPreviewImage.Source = _vehicleBitmap;
            HaloOverlayImage.Source = BuildHaloOverlay(source, processed);
            UpdateHaloOverlayVisibility();
        }
        catch (Exception ex)
        {
            ComposerStatusText.Text = "Halo adjustment could not be applied.";
            ComposerDetailText.Text = ex.Message;
        }
    }

    private static BitmapSource BuildHaloOverlay(BitmapSource original, BitmapSource processed)
    {
        var before = EnsureBgra32(original);
        var after = EnsureBgra32(processed);
        var width = after.PixelWidth;
        var height = after.PixelHeight;
        var stride = width * 4;
        var beforePixels = new byte[stride * height];
        var afterPixels = new byte[stride * height];
        var overlay = new byte[stride * height];
        before.CopyPixels(beforePixels, stride, 0);
        after.CopyPixels(afterPixels, stride, 0);

        for (var i = 0; i < width * height; i++)
        {
            var p = i * 4;
            var a = afterPixels[p + 3];
            if (a <= 3)
                continue;

            var alphaChange = Math.Abs(afterPixels[p + 3] - beforePixels[p + 3]);
            var colorChange =
                Math.Abs(afterPixels[p] - beforePixels[p]) +
                Math.Abs(afterPixels[p + 1] - beforePixels[p + 1]) +
                Math.Abs(afterPixels[p + 2] - beforePixels[p + 2]);

            var edge = a < 245;
            if (!edge && alphaChange < 8 && colorChange < 20)
                continue;

            if (colorChange > 28)
            {
                // Blue marks pixels where edge color was decontaminated.
                overlay[p] = 255;
                overlay[p + 1] = 105;
                overlay[p + 2] = 0;
                overlay[p + 3] = (byte)Math.Clamp(80 + colorChange / 3, 80, 180);
            }
            else
            {
                // Red marks the alpha transition / halo inspection zone.
                overlay[p] = 30;
                overlay[p + 1] = 45;
                overlay[p + 2] = 255;
                overlay[p + 3] = (byte)Math.Clamp(65 + alphaChange * 3, 65, 170);
            }
        }

        var result = BitmapSource.Create(
            width,
            height,
            after.DpiX,
            after.DpiY,
            PixelFormats.Bgra32,
            null,
            overlay,
            stride);
        result.Freeze();
        return result;
    }

    private static bool TryGetInteriorColor(
        byte[] pixels,
        byte[] alpha,
        int width,
        int height,
        int stride,
        int x,
        int y,
        out byte b,
        out byte g,
        out byte r)
    {
        var sumB = 0;
        var sumG = 0;
        var sumR = 0;
        var count = 0;
        const int radius = 3;

        for (var yy = Math.Max(0, y - radius); yy <= Math.Min(height - 1, y + radius); yy++)
        {
            var row = yy * stride;
            var alphaRow = yy * width;
            for (var xx = Math.Max(0, x - radius); xx <= Math.Min(width - 1, x + radius); xx++)
            {
                if (alpha[alphaRow + xx] < 220)
                    continue;

                var p = row + xx * 4;
                sumB += pixels[p];
                sumG += pixels[p + 1];
                sumR += pixels[p + 2];
                count++;
            }
        }

        if (count == 0)
        {
            b = g = r = 0;
            return false;
        }

        b = (byte)(sumB / count);
        g = (byte)(sumG / count);
        r = (byte)(sumR / count);
        return true;
    }

    private static byte[] MorphAlpha(byte[] source, int width, int height, int radius, bool dilate)
    {
        if (radius <= 0)
            return (byte[])source.Clone();

        var temp = new byte[source.Length];
        var result = new byte[source.Length];

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var best = dilate ? 0 : 255;
                var start = Math.Max(0, x - radius);
                var end = Math.Min(width - 1, x + radius);
                for (var xx = start; xx <= end; xx++)
                    best = dilate ? Math.Max(best, source[row + xx]) : Math.Min(best, source[row + xx]);
                temp[row + x] = (byte)best;
            }
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var best = dilate ? 0 : 255;
                var start = Math.Max(0, y - radius);
                var end = Math.Min(height - 1, y + radius);
                for (var yy = start; yy <= end; yy++)
                    best = dilate ? Math.Max(best, temp[yy * width + x]) : Math.Min(best, temp[yy * width + x]);
                result[y * width + x] = (byte)best;
            }
        }

        return result;
    }

    private static byte[] BoxBlurAlpha(byte[] source, int width, int height, int radius)
    {
        if (radius <= 0)
            return (byte[])source.Clone();

        var temp = new byte[source.Length];
        var result = new byte[source.Length];

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var sum = 0;
            var initialEnd = Math.Min(width - 1, radius);
            for (var x = 0; x <= initialEnd; x++)
                sum += source[row + x];

            for (var x = 0; x < width; x++)
            {
                var left = Math.Max(0, x - radius);
                var right = Math.Min(width - 1, x + radius);
                if (x > 0)
                {
                    var remove = x - radius - 1;
                    var add = x + radius;
                    if (remove >= 0) sum -= source[row + remove];
                    if (add < width) sum += source[row + add];
                }
                temp[row + x] = (byte)(sum / (right - left + 1));
            }
        }

        for (var x = 0; x < width; x++)
        {
            var sum = 0;
            var initialEnd = Math.Min(height - 1, radius);
            for (var y = 0; y <= initialEnd; y++)
                sum += temp[y * width + x];

            for (var y = 0; y < height; y++)
            {
                var top = Math.Max(0, y - radius);
                var bottom = Math.Min(height - 1, y + radius);
                if (y > 0)
                {
                    var remove = y - radius - 1;
                    var add = y + radius;
                    if (remove >= 0) sum -= temp[remove * width + x];
                    if (add < height) sum += temp[add * width + x];
                }
                result[y * width + x] = (byte)(sum / (bottom - top + 1));
            }
        }

        return result;
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
            return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static BitmapSource LimitBitmapDimension(BitmapSource source, int maxDimension)
    {
        var largest = Math.Max(source.PixelWidth, source.PixelHeight);
        if (largest <= maxDimension)
            return source;

        var scale = maxDimension / (double)largest;
        var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        transformed.Freeze();
        return transformed;
    }

    private static byte LerpByte(byte from, byte to, double amount)
        => ClampByte(from + (to - from) * Math.Clamp(amount, 0, 1));

    private static byte ClampByte(double value)
        => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private string GetHaloStatePath()
        => Path.Combine(GetProjectFolder(), _job.JobNumber + ".halo.json");

    private void SaveHaloState()
    {
        if (_restoringHaloState || !_initialized)
            return;

        try
        {
            var state = new HaloInspectorState
            {
                Feather = HaloFeatherSlider.Value,
                MaskShift = HaloMaskSlider.Value,
                HaloRemoval = HaloRemovalSlider.Value,
                ColorDecontamination = HaloDecontamSlider.Value,
                FineDetailRecovery = HaloDetailSlider.Value,
                ShowOverlay = HaloOverlayCheckBox.IsChecked == true,
                SavedAt = DateTime.Now
            };

            File.WriteAllText(
                GetHaloStatePath(),
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Halo state is convenience metadata; a save failure must not block editing/export.
        }
    }

    private bool TryRestoreHaloState()
    {
        var path = GetHaloStatePath();
        if (!File.Exists(path))
            return false;

        try
        {
            var state = JsonSerializer.Deserialize<HaloInspectorState>(File.ReadAllText(path));
            if (state is null)
                return false;

            _restoringHaloState = true;
            HaloFeatherSlider.Value = Math.Clamp(state.Feather, HaloFeatherSlider.Minimum, HaloFeatherSlider.Maximum);
            HaloMaskSlider.Value = Math.Clamp(state.MaskShift, HaloMaskSlider.Minimum, HaloMaskSlider.Maximum);
            HaloRemovalSlider.Value = Math.Clamp(state.HaloRemoval, HaloRemovalSlider.Minimum, HaloRemovalSlider.Maximum);
            HaloDecontamSlider.Value = Math.Clamp(state.ColorDecontamination, HaloDecontamSlider.Minimum, HaloDecontamSlider.Maximum);
            HaloDetailSlider.Value = Math.Clamp(state.FineDetailRecovery, HaloDetailSlider.Minimum, HaloDetailSlider.Maximum);
            HaloOverlayCheckBox.IsChecked = state.ShowOverlay;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _restoringHaloState = false;
        }
    }

    private sealed class HaloInspectorState
    {
        public double Feather { get; set; }
        public double MaskShift { get; set; }
        public double HaloRemoval { get; set; }
        public double ColorDecontamination { get; set; }
        public double FineDetailRecovery { get; set; }
        public bool ShowOverlay { get; set; }
        public DateTime SavedAt { get; set; }
    }
}
