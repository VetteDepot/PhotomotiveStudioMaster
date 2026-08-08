using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PhotomotiveStudioMaster.App;

public partial class ComposerWindow
{
    private BitmapSource? _realismBaseBitmap;
    private DispatcherTimer? _realismDebounceTimer;
    private bool _restoringRealismState;
    private bool _realismRefreshBasePending;
    private Color _groundBounceColor = Color.FromRgb(128, 128, 128);
    private Color _skyFillColor = Color.FromRgb(128, 128, 128);

    private void ComposerWindow_Phase7Loaded(object sender, RoutedEventArgs e)
    {
        ComposerWindow_Loaded(sender, e);

        if (_vehicleBitmap is null)
            return;

        _realismBaseBitmap = _vehicleBitmap;
        SubscribeToHaloChanges();
        BackgroundList.SelectionChanged += Realism_BackgroundSelectionChanged;

        TryRestoreRealismState();
        UpdateRealismValueLabels();
        UpdateSceneAnalysisText();
        ApplyRealismAdjustments();
    }

    private void SubscribeToHaloChanges()
    {
        HaloFeatherSlider.ValueChanged += HaloChangedForRealism;
        HaloMaskSlider.ValueChanged += HaloChangedForRealism;
        HaloRemovalSlider.ValueChanged += HaloChangedForRealism;
        HaloDecontamSlider.ValueChanged += HaloChangedForRealism;
        HaloDetailSlider.ValueChanged += HaloChangedForRealism;
    }

    private void HaloChangedForRealism(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized)
            return;

        QueueRealismUpdate(340, refreshBaseFromVehicle: true);
    }

    private void Realism_BackgroundSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSceneAnalysisText();
    }

    private void RealismSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateRealismValueLabels();
        if (!_initialized || _restoringRealismState || _realismBaseBitmap is null)
            return;

        QueueRealismUpdate(150, refreshBaseFromVehicle: false);
    }

    private void RealismControl_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _restoringRealismState || _realismBaseBitmap is null)
            return;

        ApplyRealismAdjustments();
        SaveRealismState();
    }

    private void QueueRealismUpdate(int delayMs, bool refreshBaseFromVehicle)
    {
        _realismDebounceTimer ??= new DispatcherTimer();
        _realismDebounceTimer.Stop();
        _realismDebounceTimer.Tick -= RealismDebounceTimer_Tick;
        _realismRefreshBasePending = refreshBaseFromVehicle;
        _realismDebounceTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _realismDebounceTimer.Tick += RealismDebounceTimer_Tick;
        _realismDebounceTimer.Start();
    }

    private void RealismDebounceTimer_Tick(object? sender, EventArgs e)
    {
        if (_realismDebounceTimer is null)
            return;

        _realismDebounceTimer.Stop();
        if (_realismRefreshBasePending && _vehicleBitmap is not null)
            _realismBaseBitmap = _vehicleBitmap;
        _realismRefreshBasePending = false;

        ApplyRealismAdjustments();
        SaveRealismState();
    }

    private void AutoLightMatch_Click(object sender, RoutedEventArgs e)
    {
        if (_backgroundBitmap is null || _realismBaseBitmap is null)
        {
            MessageBox.Show("Choose a background first.", "Auto Light Match", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var scene = AnalyzeBitmap(_backgroundBitmap);
            var car = AnalyzeBitmap(_realismBaseBitmap, ignoreTransparent: true);
            var top = AnalyzeRegion(_backgroundBitmap, 0.00, 0.30);
            var ground = AnalyzeRegion(_backgroundBitmap, 0.72, 1.00);

            _groundBounceColor = ground.AverageColor;
            _skyFillColor = top.AverageColor;

            _restoringRealismState = true;
            RealismEnabledCheckBox.IsChecked = true;

            var brightnessDelta = (scene.Luminance - car.Luminance) * 0.30;
            RealismBrightnessSlider.Value = Math.Clamp(brightnessDelta, -22, 22);

            var contrastDelta = (scene.Contrast - car.Contrast) * 0.32;
            RealismContrastSlider.Value = Math.Clamp(contrastDelta, -20, 20);

            var sceneWarmth = scene.Red - scene.Blue;
            var carWarmth = car.Red - car.Blue;
            RealismTemperatureSlider.Value = Math.Clamp((sceneWarmth - carWarmth) * 0.42, -28, 28);

            var sceneTint = scene.Green - (scene.Red + scene.Blue) / 2.0;
            var carTint = car.Green - (car.Red + car.Blue) / 2.0;
            RealismTintSlider.Value = Math.Clamp((sceneTint - carTint) * 0.30, -20, 20);

            var saturationDelta = (scene.Saturation - car.Saturation) * 0.28;
            RealismSaturationSlider.Value = Math.Clamp(saturationDelta, -20, 20);

            var groundDifference = ColorDistanceFromNeutral(ground.AverageColor);
            GroundBounceSlider.Value = Math.Clamp(groundDifference * 0.30, 6, 34);

            var skyDifference = ColorDistanceFromNeutral(top.AverageColor);
            SkyFillSlider.Value = Math.Clamp(skyDifference * 0.26, 5, 30);

            var detailRatio = car.Detail <= 0.001 ? 1.0 : scene.Detail / car.Detail;
            DofMatchSlider.Value = detailRatio < 0.72
                ? Math.Clamp((0.72 - detailRatio) * 8.0, 0, 3.5)
                : 0;

            NoiseMatchSlider.Value = Math.Clamp(scene.NoiseEstimate * 0.42, 0, 8);
            _restoringRealismState = false;

            UpdateRealismValueLabels();
            UpdateSceneAnalysisText(scene, top, ground);
            ApplyRealismAdjustments();
            SaveRealismState();

            ComposerStatusText.Text = "Auto Light Match applied.";
            ComposerDetailText.Text = "Vehicle brightness, color, ground bounce, sky fill, focus and texture were matched conservatively to the selected background.";
        }
        catch (Exception ex)
        {
            _restoringRealismState = false;
            ComposerStatusText.Text = "Auto Light Match could not analyze this background.";
            ComposerDetailText.Text = ex.Message;
        }
    }

    private void ResetRealism_Click(object sender, RoutedEventArgs e)
    {
        _restoringRealismState = true;
        RealismEnabledCheckBox.IsChecked = true;
        RealismBrightnessSlider.Value = 0;
        RealismContrastSlider.Value = 0;
        RealismTemperatureSlider.Value = 0;
        RealismTintSlider.Value = 0;
        RealismSaturationSlider.Value = 0;
        GroundBounceSlider.Value = 0;
        SkyFillSlider.Value = 0;
        DofMatchSlider.Value = 0;
        NoiseMatchSlider.Value = 0;
        _restoringRealismState = false;

        UpdateRealismValueLabels();
        ApplyRealismAdjustments();
        SaveRealismState();
        ComposerStatusText.Text = "Composite Realism controls reset.";
    }

    private void UpdateRealismValueLabels()
    {
        if (RealismBrightnessValueText is null)
            return;

        RealismBrightnessValueText.Text = $"{RealismBrightnessSlider.Value:+0;-0;0}";
        RealismContrastValueText.Text = $"{RealismContrastSlider.Value:+0;-0;0}";
        RealismTemperatureValueText.Text = $"{RealismTemperatureSlider.Value:+0;-0;0}";
        RealismTintValueText.Text = $"{RealismTintSlider.Value:+0;-0;0}";
        RealismSaturationValueText.Text = $"{RealismSaturationSlider.Value:+0;-0;0}";
        GroundBounceValueText.Text = $"{GroundBounceSlider.Value:0}%";
        SkyFillValueText.Text = $"{SkyFillSlider.Value:0}%";
        DofMatchValueText.Text = $"{DofMatchSlider.Value:0.0}px";
        NoiseMatchValueText.Text = $"{NoiseMatchSlider.Value:0}%";
    }

    private void UpdateSceneAnalysisText()
    {
        if (_backgroundBitmap is null || SceneAnalysisText is null)
        {
            if (SceneAnalysisText is not null)
                SceneAnalysisText.Text = "Scene analysis: choose a background";
            return;
        }

        try
        {
            var scene = AnalyzeBitmap(_backgroundBitmap);
            var top = AnalyzeRegion(_backgroundBitmap, 0.00, 0.30);
            var ground = AnalyzeRegion(_backgroundBitmap, 0.72, 1.00);
            UpdateSceneAnalysisText(scene, top, ground);
        }
        catch
        {
            SceneAnalysisText.Text = "Scene analysis: unavailable";
        }
    }

    private void UpdateSceneAnalysisText(ImageAnalysis scene, ImageAnalysis top, ImageAnalysis ground)
    {
        var exposure = scene.Luminance >= 165 ? "bright" : scene.Luminance <= 88 ? "dark" : "balanced";
        var warmth = scene.Red - scene.Blue;
        var temperature = warmth > 12 ? "warm" : warmth < -12 ? "cool" : "neutral";
        SceneAnalysisText.Text = $"Scene: {exposure}, {temperature}  •  Ground {ColorSummary(ground.AverageColor)}  •  Sky {ColorSummary(top.AverageColor)}";
    }

    private void ApplyRealismAdjustments()
    {
        if (_realismBaseBitmap is null)
            return;

        if (RealismEnabledCheckBox.IsChecked != true)
        {
            _vehicleBitmap = _realismBaseBitmap;
            CarPreviewImage.Source = _vehicleBitmap;
            RealismBadge.Visibility = Visibility.Collapsed;
            UpdatePlacementPreview();
            return;
        }

        try
        {
            var source = EnsureBgra32(_realismBaseBitmap);
            var width = source.PixelWidth;
            var height = source.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            source.CopyPixels(pixels, stride, 0);

            var brightness = RealismBrightnessSlider.Value * 2.0;
            var contrast = 1.0 + RealismContrastSlider.Value / 100.0;
            var temperature = RealismTemperatureSlider.Value;
            var tint = RealismTintSlider.Value;
            var saturation = 1.0 + RealismSaturationSlider.Value / 100.0;
            var groundAmount = GroundBounceSlider.Value / 100.0 * 0.34;
            var skyAmount = SkyFillSlider.Value / 100.0 * 0.30;

            for (var y = 0; y < height; y++)
            {
                var normalizedY = height <= 1 ? 0.5 : y / (double)(height - 1);
                var skyWeight = Math.Max(0, 1.0 - normalizedY / 0.62) * skyAmount;
                var groundWeight = Math.Max(0, (normalizedY - 0.48) / 0.52) * groundAmount;
                var row = y * stride;

                for (var x = 0; x < width; x++)
                {
                    var p = row + x * 4;
                    var alpha = pixels[p + 3];
                    if (alpha <= 2)
                        continue;

                    double b = pixels[p];
                    double g = pixels[p + 1];
                    double r = pixels[p + 2];

                    r += brightness;
                    g += brightness;
                    b += brightness;

                    r = 128 + (r - 128) * contrast;
                    g = 128 + (g - 128) * contrast;
                    b = 128 + (b - 128) * contrast;

                    r += temperature * 0.78;
                    b -= temperature * 0.78;

                    r += tint * 0.28;
                    b += tint * 0.28;
                    g -= tint * 0.44;

                    var lum = 0.299 * r + 0.587 * g + 0.114 * b;
                    r = lum + (r - lum) * saturation;
                    g = lum + (g - lum) * saturation;
                    b = lum + (b - lum) * saturation;

                    if (skyWeight > 0)
                    {
                        r = Lerp(r, _skyFillColor.R, skyWeight);
                        g = Lerp(g, _skyFillColor.G, skyWeight);
                        b = Lerp(b, _skyFillColor.B, skyWeight);
                    }

                    if (groundWeight > 0)
                    {
                        r = Lerp(r, _groundBounceColor.R, groundWeight);
                        g = Lerp(g, _groundBounceColor.G, groundWeight);
                        b = Lerp(b, _groundBounceColor.B, groundWeight);
                    }

                    pixels[p] = ClampByte(b);
                    pixels[p + 1] = ClampByte(g);
                    pixels[p + 2] = ClampByte(r);
                }
            }

            var processed = BitmapSource.Create(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, pixels, stride);
            processed.Freeze();

            var blurRadius = DofMatchSlider.Value;
            if (blurRadius > 0.05)
                processed = BlurBitmap(processed, blurRadius);

            var noise = NoiseMatchSlider.Value;
            if (noise > 0.05)
                processed = AddDeterministicNoise(processed, noise);

            _vehicleBitmap = processed;
            CarPreviewImage.Source = _vehicleBitmap;
            RealismBadge.Visibility = HasRealismAdjustments() ? Visibility.Visible : Visibility.Collapsed;
            UpdatePlacementPreview();
        }
        catch (Exception ex)
        {
            ComposerStatusText.Text = "Composite Realism adjustment could not be applied.";
            ComposerDetailText.Text = ex.Message;
        }
    }

    private bool HasRealismAdjustments()
    {
        return Math.Abs(RealismBrightnessSlider.Value) > 0.1 ||
               Math.Abs(RealismContrastSlider.Value) > 0.1 ||
               Math.Abs(RealismTemperatureSlider.Value) > 0.1 ||
               Math.Abs(RealismTintSlider.Value) > 0.1 ||
               Math.Abs(RealismSaturationSlider.Value) > 0.1 ||
               GroundBounceSlider.Value > 0.1 ||
               SkyFillSlider.Value > 0.1 ||
               DofMatchSlider.Value > 0.05 ||
               NoiseMatchSlider.Value > 0.1;
    }

    private static BitmapSource BlurBitmap(BitmapSource source, double radiusValue)
    {
        var bgra = EnsureBgra32(source);
        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        var radius = Math.Clamp((int)Math.Round(radiusValue), 1, 8);
        var horizontal = new byte[pixels.Length];
        var result = new byte[pixels.Length];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var start = Math.Max(0, x - radius);
                var end = Math.Min(width - 1, x + radius);
                var count = end - start + 1;
                var sums = new int[4];
                for (var xx = start; xx <= end; xx++)
                {
                    var p = y * stride + xx * 4;
                    for (var c = 0; c < 4; c++) sums[c] += pixels[p + c];
                }
                var d = y * stride + x * 4;
                for (var c = 0; c < 4; c++) horizontal[d + c] = (byte)(sums[c] / count);
            }
        }

        for (var y = 0; y < height; y++)
        {
            var startY = Math.Max(0, y - radius);
            var endY = Math.Min(height - 1, y + radius);
            var count = endY - startY + 1;
            for (var x = 0; x < width; x++)
            {
                var sums = new int[4];
                for (var yy = startY; yy <= endY; yy++)
                {
                    var p = yy * stride + x * 4;
                    for (var c = 0; c < 4; c++) sums[c] += horizontal[p + c];
                }
                var d = y * stride + x * 4;
                for (var c = 0; c < 4; c++) result[d + c] = (byte)(sums[c] / count);
            }
        }

        var bitmap = BitmapSource.Create(width, height, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null, result, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource AddDeterministicNoise(BitmapSource source, double strength)
    {
        var bgra = EnsureBgra32(source);
        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);
        var amount = Math.Clamp(strength * 0.58, 0, 12);

        uint seed = 0x9E3779B9u;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var p = row + x * 4;
                if (pixels[p + 3] <= 2)
                    continue;

                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                var unit = (seed & 0xFFFF) / 65535.0;
                var delta = (unit - 0.5) * 2.0 * amount;
                pixels[p] = ClampByte(pixels[p] + delta);
                pixels[p + 1] = ClampByte(pixels[p + 1] + delta);
                pixels[p + 2] = ClampByte(pixels[p + 2] + delta);
            }
        }

        var result = BitmapSource.Create(width, height, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static ImageAnalysis AnalyzeBitmap(BitmapSource source, bool ignoreTransparent = false)
        => AnalyzePixels(source, 0, source.PixelHeight, ignoreTransparent);

    private static ImageAnalysis AnalyzeRegion(BitmapSource source, double topFraction, double bottomFraction)
    {
        var top = Math.Clamp((int)Math.Round(source.PixelHeight * topFraction), 0, Math.Max(0, source.PixelHeight - 1));
        var bottom = Math.Clamp((int)Math.Round(source.PixelHeight * bottomFraction), top + 1, source.PixelHeight);
        return AnalyzePixels(source, top, bottom, false);
    }

    private static ImageAnalysis AnalyzePixels(BitmapSource source, int top, int bottom, bool ignoreTransparent)
    {
        var bgra = EnsureBgra32(LimitBitmapDimension(source, 900));
        var width = bgra.PixelWidth;
        var height = bgra.PixelHeight;
        top = Math.Clamp(top * height / Math.Max(1, source.PixelHeight), 0, height - 1);
        bottom = Math.Clamp(bottom * height / Math.Max(1, source.PixelHeight), top + 1, height);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        double sumR = 0, sumG = 0, sumB = 0, sumLum = 0, sumLumSq = 0, sumSat = 0, detail = 0, noise = 0;
        long count = 0;
        var step = Math.Max(1, Math.Min(width, height) / 320);

        for (var y = top; y < bottom; y += step)
        {
            var row = y * stride;
            for (var x = 0; x < width; x += step)
            {
                var p = row + x * 4;
                var a = pixels[p + 3];
                if (ignoreTransparent && a < 32)
                    continue;

                var b = pixels[p];
                var g = pixels[p + 1];
                var r = pixels[p + 2];
                var lum = 0.299 * r + 0.587 * g + 0.114 * b;
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                var sat = max <= 1 ? 0 : (max - min) * 100.0 / max;

                sumR += r; sumG += g; sumB += b;
                sumLum += lum; sumLumSq += lum * lum; sumSat += sat;
                count++;

                if (x + step < width)
                {
                    var q = row + (x + step) * 4;
                    var neighborLum = 0.299 * pixels[q + 2] + 0.587 * pixels[q + 1] + 0.114 * pixels[q];
                    detail += Math.Abs(lum - neighborLum);
                }
                if (x > 0 && x + step < width)
                {
                    var left = row + Math.Max(0, x - step) * 4;
                    var right = row + Math.Min(width - 1, x + step) * 4;
                    var ll = 0.299 * pixels[left + 2] + 0.587 * pixels[left + 1] + 0.114 * pixels[left];
                    var rr = 0.299 * pixels[right + 2] + 0.587 * pixels[right + 1] + 0.114 * pixels[right];
                    noise += Math.Abs(lum - (ll + rr) / 2.0);
                }
            }
        }

        if (count == 0)
            return new ImageAnalysis(128, 0, 128, 128, 128, 0, 0, 0, Color.FromRgb(128, 128, 128));

        var meanLum = sumLum / count;
        var variance = Math.Max(0, sumLumSq / count - meanLum * meanLum);
        var avgR = sumR / count;
        var avgG = sumG / count;
        var avgB = sumB / count;
        var color = Color.FromRgb(ClampByte(avgR), ClampByte(avgG), ClampByte(avgB));
        return new ImageAnalysis(meanLum, Math.Sqrt(variance), avgR, avgG, avgB, sumSat / count, detail / count, noise / count, color);
    }

    private static double ColorDistanceFromNeutral(Color color)
    {
        var mean = (color.R + color.G + color.B) / 3.0;
        return (Math.Abs(color.R - mean) + Math.Abs(color.G - mean) + Math.Abs(color.B - mean)) / 3.0;
    }

    private static string ColorSummary(Color color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        var min = Math.Min(color.R, Math.Min(color.G, color.B));
        if (max - min < 12) return "neutral";
        if (color.R == max && color.B == min) return "warm";
        if (color.B == max) return "cool";
        if (color.G == max) return "green";
        return "mixed";
    }

    private static double Lerp(double from, double to, double amount) => from + (to - from) * Math.Clamp(amount, 0, 1);

    private void SaveRealismState()
    {
        if (_restoringRealismState)
            return;

        try
        {
            var state = new RealismState
            {
                Enabled = RealismEnabledCheckBox.IsChecked == true,
                Brightness = RealismBrightnessSlider.Value,
                Contrast = RealismContrastSlider.Value,
                Temperature = RealismTemperatureSlider.Value,
                Tint = RealismTintSlider.Value,
                Saturation = RealismSaturationSlider.Value,
                GroundBounce = GroundBounceSlider.Value,
                SkyFill = SkyFillSlider.Value,
                DepthOfField = DofMatchSlider.Value,
                Noise = NoiseMatchSlider.Value,
                GroundR = _groundBounceColor.R,
                GroundG = _groundBounceColor.G,
                GroundB = _groundBounceColor.B,
                SkyR = _skyFillColor.R,
                SkyG = _skyFillColor.G,
                SkyB = _skyFillColor.B,
                SavedAt = DateTime.Now
            };
            File.WriteAllText(GetRealismStatePath(), JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void TryRestoreRealismState()
    {
        var path = GetRealismStatePath();
        if (!File.Exists(path))
            return;

        try
        {
            var state = JsonSerializer.Deserialize<RealismState>(File.ReadAllText(path));
            if (state is null)
                return;

            _restoringRealismState = true;
            RealismEnabledCheckBox.IsChecked = state.Enabled;
            RealismBrightnessSlider.Value = Math.Clamp(state.Brightness, RealismBrightnessSlider.Minimum, RealismBrightnessSlider.Maximum);
            RealismContrastSlider.Value = Math.Clamp(state.Contrast, RealismContrastSlider.Minimum, RealismContrastSlider.Maximum);
            RealismTemperatureSlider.Value = Math.Clamp(state.Temperature, RealismTemperatureSlider.Minimum, RealismTemperatureSlider.Maximum);
            RealismTintSlider.Value = Math.Clamp(state.Tint, RealismTintSlider.Minimum, RealismTintSlider.Maximum);
            RealismSaturationSlider.Value = Math.Clamp(state.Saturation, RealismSaturationSlider.Minimum, RealismSaturationSlider.Maximum);
            GroundBounceSlider.Value = Math.Clamp(state.GroundBounce, GroundBounceSlider.Minimum, GroundBounceSlider.Maximum);
            SkyFillSlider.Value = Math.Clamp(state.SkyFill, SkyFillSlider.Minimum, SkyFillSlider.Maximum);
            DofMatchSlider.Value = Math.Clamp(state.DepthOfField, DofMatchSlider.Minimum, DofMatchSlider.Maximum);
            NoiseMatchSlider.Value = Math.Clamp(state.Noise, NoiseMatchSlider.Minimum, NoiseMatchSlider.Maximum);
            _groundBounceColor = Color.FromRgb(state.GroundR, state.GroundG, state.GroundB);
            _skyFillColor = Color.FromRgb(state.SkyR, state.SkyG, state.SkyB);
        }
        catch { }
        finally
        {
            _restoringRealismState = false;
        }
    }

    private string GetRealismStatePath() => Path.Combine(GetProjectFolder(), _job.JobNumber + ".realism.json");

    private readonly record struct ImageAnalysis(double Luminance, double Contrast, double Red, double Green, double Blue, double Saturation, double Detail, double NoiseEstimate, Color AverageColor);

    private sealed class RealismState
    {
        public bool Enabled { get; set; } = true;
        public double Brightness { get; set; }
        public double Contrast { get; set; }
        public double Temperature { get; set; }
        public double Tint { get; set; }
        public double Saturation { get; set; }
        public double GroundBounce { get; set; }
        public double SkyFill { get; set; }
        public double DepthOfField { get; set; }
        public double Noise { get; set; }
        public byte GroundR { get; set; } = 128;
        public byte GroundG { get; set; } = 128;
        public byte GroundB { get; set; } = 128;
        public byte SkyR { get; set; } = 128;
        public byte SkyG { get; set; } = 128;
        public byte SkyB { get; set; } = 128;
        public DateTime SavedAt { get; set; }
    }
}
