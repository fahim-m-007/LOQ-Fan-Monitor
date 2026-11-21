using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using LOQFanMonitor.Services;

namespace LOQFanMonitor;

public partial class MainWindow : Window
{
    private readonly LenovoWmiReader _reader = new();

    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    // Complete session history.
    private readonly List<(DateTime Time, uint? Fan1, uint? Fan2)> _history = new();

    private readonly Stopwatch _sessionStopwatch = new();

    private readonly Forms.NotifyIcon _trayIcon;

    // Keep the custom icon alive for the application lifetime.
    private readonly System.Drawing.Icon _trayApplicationIcon;

    private uint? _latestFan1Rpm;
    private uint? _latestFan2Rpm;
    private int? _latestCpuTempC;
    private int? _latestGpuTempC;

    private uint? _peakFan1Rpm;
    private uint? _peakFan2Rpm;
    private int? _peakCpuTempC;
    private int? _peakGpuTempC;

    private long _fan1RpmSum;
    private int _fan1RpmCount;
    private long _fan2RpmSum;
    private int _fan2RpmCount;

    private long _cpuTempSum;
    private int _cpuTempCount;
    private long _gpuTempSum;
    private int _gpuTempCount;

    private readonly Stopwatch _animStopwatch = new();
    private double _lastAnimSeconds;
    private double _fan1Angle;
    private double _fan2Angle;
    private bool _isRenderingHooked;
    private bool _isPolling;
    private bool _isClosed;

    private static readonly SolidColorBrush StatusGreenBrush = CreateFrozenBrush(56, 201, 154);
    private static readonly SolidColorBrush StatusAmberBrush = CreateFrozenBrush(244, 184, 96);
    private static readonly SolidColorBrush StatusRedBrush = CreateFrozenBrush(235, 92, 92);
    private static readonly SolidColorBrush GridStrokeBrush = CreateFrozenBrush(38, 52, 77);
    private static readonly SolidColorBrush AxisLabelBrush = CreateFrozenBrush(138, 153, 179);
    private static readonly DoubleCollection GridDashArray = CreateFrozenDashArray();
    private static readonly SolidColorBrush Fan1LineBrush = CreateFrozenBrush(79, 140, 255);
    private static readonly SolidColorBrush Fan2LineBrush = CreateFrozenBrush(56, 201, 154);
    private static readonly System.Windows.Media.Brush Fan1AreaBrush = CreateFrozenVerticalAreaGradient(79, 140, 255);
    private static readonly System.Windows.Media.Brush Fan2AreaBrush = CreateFrozenVerticalAreaGradient(56, 201, 154);
    private static readonly System.Windows.Media.Brush TempCoolBrush = CreateFrozenGradient(35, 168, 125, 73, 226, 177);
    private static readonly System.Windows.Media.Brush TempWarmBrush = CreateFrozenGradient(230, 154, 40, 248, 200, 88);
    private static readonly System.Windows.Media.Brush TempHotBrush = CreateFrozenGradient(235, 92, 92, 255, 133, 102);

    private const int ChartWindowSeconds = 180;
    private const double MaxFanGaugeRpm = 5800.0;
    private const double MaxTempGaugeC = 105.0;

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static DoubleCollection CreateFrozenDashArray()
    {
        var dashes = new DoubleCollection { 3, 4 };
        dashes.Freeze();
        return dashes;
    }

    private static LinearGradientBrush CreateFrozenVerticalAreaGradient(byte r, byte g, byte b)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(88, r, g, b), 0.0));
        brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(6, r, g, b), 1.0));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush CreateFrozenGradient(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        var brush = new LinearGradientBrush(
            System.Windows.Media.Color.FromRgb(r1, g1, b1),
            System.Windows.Media.Color.FromRgb(r2, g2, b2),
            0.0);
        brush.Freeze();
        return brush;
    }

    public MainWindow()
    {
        InitializeComponent();

        // Load the custom application icon.
        _trayApplicationIcon = LoadTrayIcon();

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "LOQ Fan Monitor",
            Icon = _trayApplicationIcon,
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };

        _trayIcon.ContextMenuStrip.Items.Add(
            "Open", null, (_, _) => RestoreFromTray());

        _trayIcon.ContextMenuStrip.Items.Add(
            "Exit", null, (_, _) => Close());

        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                RestoreFromTray();
            }
        };

        _timer.Tick += async (_, _) => await RefreshTelemetryAsync();

        Loaded += async (_, _) =>
        {
            _sessionStopwatch.Start();
            StartFanAnimation();
            await RefreshTelemetryAsync();
            _timer.Start();
        };
    }

    // Loads the embedded ICO resource.
    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri(
                    "pack://application:,,,/Assets/LOQFanMonitor.ico",
                    UriKind.Absolute
                )
            );

            if (resource?.Stream is not null)
            {
                using (var stream = resource.Stream)
                {
                    return new System.Drawing.Icon(stream);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Custom tray icon error: {ex.Message}");
        }

        // Fallback if the custom icon cannot be loaded.
        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private async Task RefreshTelemetryAsync()
    {
        if (_isPolling)
            return;

        _isPolling = true;
        try
        {
            TelemetrySnapshot reading = await Task.Run(() => _reader.Read());

            if (_isClosed)
                return;

            _latestFan1Rpm = reading.Fan1Rpm;
            _latestFan2Rpm = reading.Fan2Rpm;
            _latestCpuTempC = reading.CpuTempC;
            _latestGpuTempC = reading.GpuTempC;

            if (reading.Fan1Rpm.HasValue)
            {
                _fan1RpmSum += reading.Fan1Rpm.Value;
                _fan1RpmCount++;

                if (!_peakFan1Rpm.HasValue || reading.Fan1Rpm.Value > _peakFan1Rpm.Value)
                {
                    _peakFan1Rpm = reading.Fan1Rpm.Value;
                }
            }

            if (reading.Fan2Rpm.HasValue)
            {
                _fan2RpmSum += reading.Fan2Rpm.Value;
                _fan2RpmCount++;

                if (!_peakFan2Rpm.HasValue || reading.Fan2Rpm.Value > _peakFan2Rpm.Value)
                {
                    _peakFan2Rpm = reading.Fan2Rpm.Value;
                }
            }

            if (reading.CpuTempC.HasValue)
            {
                _cpuTempSum += reading.CpuTempC.Value;
                _cpuTempCount++;

                if (!_peakCpuTempC.HasValue || reading.CpuTempC.Value > _peakCpuTempC.Value)
                {
                    _peakCpuTempC = reading.CpuTempC.Value;
                }
            }

            if (reading.GpuTempC.HasValue)
            {
                _gpuTempSum += reading.GpuTempC.Value;
                _gpuTempCount++;

                if (!_peakGpuTempC.HasValue || reading.GpuTempC.Value > _peakGpuTempC.Value)
                {
                    _peakGpuTempC = reading.GpuTempC.Value;
                }
            }

            Fan1Text.Text = reading.Fan1Rpm?.ToString("N0") ?? "N/A";
            Fan2Text.Text = reading.Fan2Rpm?.ToString("N0") ?? "N/A";

            Fan1PercentText.Text = reading.Fan1Rpm.HasValue
                ? $"{Math.Clamp((int)Math.Round(reading.Fan1Rpm.Value / MaxFanGaugeRpm * 100.0), 0, 100)}%"
                : "--%";

            Fan2PercentText.Text = reading.Fan2Rpm.HasValue
                ? $"{Math.Clamp((int)Math.Round(reading.Fan2Rpm.Value / MaxFanGaugeRpm * 100.0), 0, 100)}%"
                : "--%";

            Fan1AvgText.Text = _fan1RpmCount > 0
                ? $"Avg: {(int)Math.Round((double)_fan1RpmSum / _fan1RpmCount):N0} RPM"
                : "Avg: -- RPM";

            Fan2AvgText.Text = _fan2RpmCount > 0
                ? $"Avg: {(int)Math.Round((double)_fan2RpmSum / _fan2RpmCount):N0} RPM"
                : "Avg: -- RPM";

            Fan1PeakText.Text = _peakFan1Rpm.HasValue
                ? $"Peak: {_peakFan1Rpm.Value:N0} RPM"
                : "Peak: -- RPM";

            Fan2PeakText.Text = _peakFan2Rpm.HasValue
                ? $"Peak: {_peakFan2Rpm.Value:N0} RPM"
                : "Peak: -- RPM";

            CpuTempText.Text = reading.CpuTempC.HasValue
                ? $"{reading.CpuTempC} \u00B0C"
                : "-- \u00B0C";

            GpuTempText.Text = reading.GpuTempC.HasValue
                ? $"{reading.GpuTempC} \u00B0C"
                : "-- \u00B0C";

            CpuAvgText.Text = _cpuTempCount > 0
                ? $"Avg: {(int)Math.Round((double)_cpuTempSum / _cpuTempCount)} \u00B0C"
                : "Avg: -- \u00B0C";

            GpuAvgText.Text = _gpuTempCount > 0
                ? $"Avg: {(int)Math.Round((double)_gpuTempSum / _gpuTempCount)} \u00B0C"
                : "Avg: -- \u00B0C";

            CpuPeakText.Text = _peakCpuTempC.HasValue
                ? $"Peak: {_peakCpuTempC.Value} \u00B0C"
                : "Peak: -- \u00B0C";

            GpuPeakText.Text = _peakGpuTempC.HasValue
                ? $"Peak: {_peakGpuTempC.Value} \u00B0C"
                : "Peak: -- \u00B0C";

            UpdateGaugeBars();
            UpdateTrayTooltip(reading);

            _history.Add((
                reading.Timestamp,
                reading.Fan1Rpm,
                reading.Fan2Rpm
            ));

            if (_history.Count > ChartWindowSeconds)
            {
                _history.RemoveRange(0, _history.Count - ChartWindowSeconds);
            }

            if (IsVisible)
            {
                DrawHistory();
            }

            bool fansAvailable =
                reading.Fan1Rpm.HasValue ||
                reading.Fan2Rpm.HasValue;

            if (fansAvailable)
            {
                StatusDot.Fill = StatusGreenBrush;
                LiveBadgeText.Text = "LIVE";
                LiveBadgeText.Foreground = StatusGreenBrush;
                StatusText.Text = "Connected to Lenovo fan telemetry";
            }
            else
            {
                StatusDot.Fill = StatusAmberBrush;
                LiveBadgeText.Text = "UNAVAILABLE";
                LiveBadgeText.Foreground = StatusAmberBrush;
                StatusText.Text = "Lenovo fan telemetry is unavailable. Check permissions and WMI support.";
            }

            HistoryCaption.Text =
                $"Session: {FormatElapsedTime(_sessionStopwatch.Elapsed)}";
        }
        catch (Exception ex)
        {
            if (_isClosed)
                return;

            StatusText.Text = "Telemetry read failed: " + ex.Message;

            StatusDot.Fill = StatusRedBrush;
            LiveBadgeText.Text = "ERROR";
            LiveBadgeText.Foreground = StatusRedBrush;
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void UpdateTrayTooltip(TelemetrySnapshot reading)
    {
        if (_isClosed)
            return;

        string cpuTemp = reading.CpuTempC.HasValue ? $"{reading.CpuTempC}\u00B0C" : "--\u00B0C";
        string gpuTemp = reading.GpuTempC.HasValue ? $"{reading.GpuTempC}\u00B0C" : "--\u00B0C";
        string cpuRpm = reading.Fan2Rpm.HasValue ? $"{reading.Fan2Rpm.Value}" : "--";
        string gpuRpm = reading.Fan1Rpm.HasValue ? $"{reading.Fan1Rpm.Value}" : "--";

        // Windows Forms NotifyIcon.Text has a strict 63-character limit.
        string tooltip = $"LOQ Monitor\nCPU: {cpuTemp} ({cpuRpm} RPM)\nGPU: {gpuTemp} ({gpuRpm} RPM)";
        if (tooltip.Length > 63)
        {
            tooltip = tooltip[..63];
        }

        try
        {
            _trayIcon.Text = tooltip;
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void UpdateGaugeBars()
    {
        SetBarWidth(Fan1BarTrack, Fan1BarFill, (_latestFan1Rpm ?? 0) / MaxFanGaugeRpm);
        SetBarWidth(Fan2BarTrack, Fan2BarFill, (_latestFan2Rpm ?? 0) / MaxFanGaugeRpm);

        SetBarWidth(CpuBarTrack, CpuBarFill, (_latestCpuTempC ?? 0) / MaxTempGaugeC);
        CpuBarFill.Background = GetTemperatureBrush(_latestCpuTempC);

        SetBarWidth(GpuBarTrack, GpuBarFill, (_latestGpuTempC ?? 0) / MaxTempGaugeC);
        GpuBarFill.Background = GetTemperatureBrush(_latestGpuTempC);
    }

    private static void SetBarWidth(Border track, Border fill, double ratio)
    {
        double availableWidth = Math.Max(0, track.ActualWidth - 2);
        double clamped = Math.Clamp(ratio, 0.0, 1.0);
        fill.Width = availableWidth * clamped;
    }

    private static System.Windows.Media.Brush GetTemperatureBrush(int? tempC)
    {
        if (!tempC.HasValue)
        {
            return StatusGreenBrush;
        }

        if (tempC.Value >= 85)
        {
            return TempHotBrush;
        }

        if (tempC.Value >= 70)
        {
            return TempWarmBrush;
        }

        return TempCoolBrush;
    }

    private void GaugeTrack_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        UpdateGaugeBars();
    }

    private static string FormatElapsedTime(TimeSpan elapsed)
    {
        int days = (int)elapsed.TotalDays;
        int hours = elapsed.Hours;
        int minutes = elapsed.Minutes;
        int seconds = elapsed.Seconds;

        if (days > 0)
        {
            return $"{days} {Unit(days, "day")} " +
                   $"{hours} {Unit(hours, "hour")}";
        }

        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours} " +
                   $"{Unit((int)elapsed.TotalHours, "hour")}" +
                   (minutes > 0 ? $" {minutes} {Unit(minutes, "minute")}" : "");
        }

        if (elapsed.TotalMinutes >= 1)
        {
            return $"{(int)elapsed.TotalMinutes} " +
                   $"{Unit((int)elapsed.TotalMinutes, "minute")}";
        }

        return $"{Math.Max(0, seconds)} {Unit(seconds, "second")}";
    }

    private static string Unit(int value, string name)
    {
        return value == 1 ? name : name + "s";
    }

    private void DrawHistory()
    {
        HistoryCanvas.Children.Clear();

        double totalWidth = HistoryCanvas.ActualWidth;
        double totalHeight = HistoryCanvas.ActualHeight;

        const double leftAxisMargin = 44.0;
        const double topPad = 8.0;
        const double bottomPad = 10.0;

        double plotWidth = totalWidth - leftAxisMargin;
        double plotHeight = totalHeight - topPad - bottomPad;

        if (plotWidth < 20 || plotHeight < 20)
            return;

        var points = _history
            .TakeLast(ChartWindowSeconds)
            .ToArray();

        double max = Math.Max(MaxFanGaugeRpm, points
            .SelectMany(p => new[]
            {
                (double)(p.Fan1 ?? 0),
                (double)(p.Fan2 ?? 0)
            })
            .DefaultIfEmpty(MaxFanGaugeRpm)
            .Max());

        // Round max nicely to the nearest 200 if a spike exceeds 5800
        if (max > MaxFanGaugeRpm)
        {
            max = Math.Ceiling(max / 200.0) * 200.0;
        }

        // Draw 5 horizontal dashed grid lines (0%, 25%, 50%, 75%, 100%) + Y-axis labels
        for (int i = 0; i <= 4; i++)
        {
            double y = topPad + (plotHeight * i / 4.0);
            int labelRpm = (int)Math.Round(max * (4 - i) / 4.0);

            var label = new TextBlock
            {
                Text = labelRpm.ToString(),
                Foreground = AxisLabelBrush,
                FontSize = 11.5,
                TextAlignment = TextAlignment.Right,
                Width = leftAxisMargin - 10
            };

            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 8);
            HistoryCanvas.Children.Add(label);

            HistoryCanvas.Children.Add(new Line
            {
                X1 = leftAxisMargin,
                X2 = totalWidth,
                Y1 = y,
                Y2 = y,
                Stroke = GridStrokeBrush,
                StrokeThickness = 1,
                StrokeDashArray = GridDashArray
            });
        }

        if (_history.Count < 2)
            return;

        DrawSeries(
            points.Select(p => p.Fan1),
            leftAxisMargin,
            topPad,
            plotWidth,
            plotHeight,
            max,
            Fan1LineBrush,
            Fan1AreaBrush);

        DrawSeries(
            points.Select(p => p.Fan2),
            leftAxisMargin,
            topPad,
            plotWidth,
            plotHeight,
            max,
            Fan2LineBrush,
            Fan2AreaBrush);
    }

    private void DrawSeries(
        IEnumerable<uint?> values,
        double offsetX,
        double offsetY,
        double plotWidth,
        double plotHeight,
        double max,
        System.Windows.Media.Brush strokeBrush,
        System.Windows.Media.Brush areaFill)
    {
        var data = values.ToArray();
        var lineGeometry = new StreamGeometry();
        var areaGeometry = new StreamGeometry();

        double baselineY = offsetY + plotHeight;

        using (var lineCtx = lineGeometry.Open())
        using (var areaCtx = areaGeometry.Open())
        {
            bool started = false;
            System.Windows.Point firstPoint = default;
            System.Windows.Point lastPoint = default;

            for (int i = 0; i < data.Length; i++)
            {
                if (!data[i].HasValue)
                {
                    if (started)
                    {
                        areaCtx.LineTo(new System.Windows.Point(lastPoint.X, baselineY), false, false);
                        areaCtx.LineTo(new System.Windows.Point(firstPoint.X, baselineY), false, false);
                        started = false;
                    }
                    continue;
                }

                double x = offsetX + (data.Length <= 1
                    ? 0
                    : i * plotWidth / (data.Length - 1));

                double y = offsetY + plotHeight -
                    (data[i]!.Value / max * plotHeight);

                var point = new System.Windows.Point(x, y);

                if (!started)
                {
                    lineCtx.BeginFigure(point, false, false);
                    areaCtx.BeginFigure(point, true, true);
                    firstPoint = point;
                    lastPoint = point;
                    started = true;
                }
                else
                {
                    lineCtx.LineTo(point, true, true);
                    areaCtx.LineTo(point, true, true);
                    lastPoint = point;
                }
            }

            if (started)
            {
                areaCtx.LineTo(new System.Windows.Point(lastPoint.X, baselineY), false, false);
                areaCtx.LineTo(new System.Windows.Point(firstPoint.X, baselineY), false, false);
            }
        }

        lineGeometry.Freeze();
        areaGeometry.Freeze();

        HistoryCanvas.Children.Add(
            new System.Windows.Shapes.Path
            {
                Data = areaGeometry,
                Fill = areaFill
            });

        HistoryCanvas.Children.Add(
            new System.Windows.Shapes.Path
            {
                Data = lineGeometry,
                Stroke = strokeBrush,
                StrokeThickness = 2.4,
                StrokeLineJoin = PenLineJoin.Round
            });
    }

    private void HistoryCanvas_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        DrawHistory();
    }

    private void StartFanAnimation()
    {
        if (_isRenderingHooked)
            return;

        _animStopwatch.Restart();
        _lastAnimSeconds = 0;
        CompositionTarget.Rendering += OnCompositionTargetRendering;
        _isRenderingHooked = true;
    }

    private void StopFanAnimation()
    {
        if (!_isRenderingHooked)
            return;

        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        _animStopwatch.Stop();
        _isRenderingHooked = false;
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        double nowSeconds = _animStopwatch.Elapsed.TotalSeconds;
        double deltaSeconds = nowSeconds - _lastAnimSeconds;
        _lastAnimSeconds = nowSeconds;

        if (deltaSeconds <= 0 || deltaSeconds > 0.25)
            return;

        uint fan1Rpm = _latestFan1Rpm ?? 0;
        if (fan1Rpm > 0)
        {
            double ratio1 = Math.Clamp(fan1Rpm / MaxFanGaugeRpm, 0.05, 1.2);
            double degPerSec1 = 60.0 + (ratio1 * 660.0);
            _fan1Angle = (_fan1Angle + (degPerSec1 * deltaSeconds)) % 360.0;
            Fan1Rotation.Angle = _fan1Angle;
        }

        uint fan2Rpm = _latestFan2Rpm ?? 0;
        if (fan2Rpm > 0)
        {
            double ratio2 = Math.Clamp(fan2Rpm / MaxFanGaugeRpm, 0.05, 1.2);
            double degPerSec2 = 60.0 + (ratio2 * 660.0);
            _fan2Angle = (_fan2Angle + (degPerSec2 * deltaSeconds)) % 360.0;
            Fan2Rotation.Angle = _fan2Angle;
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void HideToTray()
    {
        StopFanAnimation();
        Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        DrawHistory();
        StartFanAnimation();
    }

    private void Window_Closing(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        _isClosed = true;
        StopFanAnimation();
        _timer.Stop();
        _sessionStopwatch.Stop();

        if (_trayIcon.ContextMenuStrip is not null)
        {
            _trayIcon.ContextMenuStrip.Dispose();
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        _trayApplicationIcon.Dispose();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _sessionStopwatch.Stop();

        base.OnClosed(e);
        System.Windows.Application.Current.Shutdown();
    }
}
