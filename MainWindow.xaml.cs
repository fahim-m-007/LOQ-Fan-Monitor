
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

    private bool _reallyExit;

    private const int ChartWindowSeconds = 180;

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
            "Exit", null, (_, _) => ExitApplication());

        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        _timer.Tick += (_, _) => RefreshTelemetry();

        Loaded += (_, _) =>
        {
            _sessionStopwatch.Start();
            RefreshTelemetry();
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
                using (var sourceIcon = new System.Drawing.Icon(stream))
                {
                    return (System.Drawing.Icon)sourceIcon.Clone();
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

    private void RefreshTelemetry()
    {
        try
        {
            TelemetrySnapshot reading = _reader.Read();

            Fan1Text.Text = reading.Fan1Rpm?.ToString("N0") ?? "N/A";
            Fan2Text.Text = reading.Fan2Rpm?.ToString("N0") ?? "N/A";

            Fan1StateText.Text = reading.Fan1Rpm.HasValue
                ? "Live Lenovo sensor"
                : "Sensor unavailable";

            Fan2StateText.Text = reading.Fan2Rpm.HasValue
                ? "Live Lenovo sensor"
                : "Sensor unavailable";

            CpuTempText.Text = reading.CpuTempC.HasValue
                ? $"{reading.CpuTempC} \u00B0C"
                : "-- \u00B0C";

            GpuTempText.Text = reading.GpuTempC.HasValue
                ? $"{reading.GpuTempC} \u00B0C"
                : "-- \u00B0C";

            _history.Add((
                reading.Timestamp,
                reading.Fan1Rpm,
                reading.Fan2Rpm
            ));

            DrawHistory();

            UpdatedText.Text = $"Updated {reading.Timestamp:HH:mm:ss}";

            bool fansAvailable =
                reading.Fan1Rpm.HasValue ||
                reading.Fan2Rpm.HasValue;

            StatusDot.Fill = fansAvailable
                ? new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(56, 201, 154))
                : new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(244, 184, 96));

            StatusText.Text = fansAvailable
                ? "Connected to Lenovo fan telemetry"
                : "Lenovo fan telemetry is unavailable. Check permissions and WMI support.";

            HistoryCaption.Text =
                $"Session history: {FormatElapsedTime(_sessionStopwatch.Elapsed)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Telemetry read failed: " + ex.Message;

            StatusDot.Fill = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(235, 92, 92));
        }
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

        double width = HistoryCanvas.ActualWidth;
        double height = HistoryCanvas.ActualHeight;

        if (width < 20 || height < 20 || _history.Count < 2)
            return;

        for (int i = 1; i <= 3; i++)
        {
            double y = height * i / 4;

            HistoryCanvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(39, 53, 77)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 4 }
            });
        }

        var points = _history
            .TakeLast(ChartWindowSeconds)
            .ToArray();

        double max = Math.Max(6000, points
            .SelectMany(p => new[]
            {
                (double)(p.Fan1 ?? 0),
                (double)(p.Fan2 ?? 0)
            })
            .DefaultIfEmpty(6000)
            .Max() * 1.12);

        DrawSeries(
            points.Select(p => p.Fan1),
            width,
            height,
            max,
            System.Windows.Media.Color.FromRgb(79, 140, 255));

        DrawSeries(
            points.Select(p => p.Fan2),
            width,
            height,
            max,
            System.Windows.Media.Color.FromRgb(56, 201, 154));
    }

    private void DrawSeries(
        IEnumerable<uint?> values,
        double width,
        double height,
        double max,
        System.Windows.Media.Color color)
    {
        var data = values.ToArray();
        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            bool started = false;

            for (int i = 0; i < data.Length; i++)
            {
                if (!data[i].HasValue)
                {
                    started = false;
                    continue;
                }

                double x = data.Length <= 1
                    ? 0
                    : i * width / (data.Length - 1);

                double y = height -
                    (data[i]!.Value / max * height);

                var point = new System.Windows.Point(x, y);

                if (!started)
                {
                    context.BeginFigure(point, false, false);
                    started = true;
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();

        HistoryCanvas.Children.Add(
            new System.Windows.Shapes.Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round
            });
    }

    private void HistoryCanvas_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        DrawHistory();
    }

    private void MinimizeToTray_Click(
        object sender,
        RoutedEventArgs e)
    {
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();

        _trayIcon.ShowBalloonTip(
            1200,
            "LOQ Fan Monitor",
            "The monitor is still running in the system tray.",
            Forms.ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _reallyExit = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_Closing(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (!_reallyExit)
        {
            e.Cancel = true;
            HideToTray();
        }
        else
        {
            _timer.Stop();
            _sessionStopwatch.Stop();

            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            _trayApplicationIcon.Dispose();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _sessionStopwatch.Stop();

        base.OnClosed(e);
    }
}
