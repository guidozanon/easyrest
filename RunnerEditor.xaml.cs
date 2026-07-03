using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EasyRest;

public partial class RunnerEditor : UserControl
{
    RunnerTab? _hooked;

    public RunnerEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rehook();
        Loaded += (_, _) => RedrawChart();
        Unloaded += (_, _) => Unhook();
    }

    RunnerTab? Vm => DataContext as RunnerTab;

    void Rehook()
    {
        Unhook();
        if (Vm is { } vm)
        {
            _hooked = vm;
            vm.Updated += OnRunnerUpdated;
        }
        RedrawChart();
    }

    void Unhook()
    {
        if (_hooked != null) _hooked.Updated -= OnRunnerUpdated;
        _hooked = null;
    }

    void OnRunnerUpdated()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(OnRunnerUpdated);
            return;
        }
        RedrawChart();
        if (Vm is { Results.Count: > 0 } vm)
            ResultsGrid.ScrollIntoView(vm.Results[^1]);
    }

    async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.RunAsync();
    }

    void Stop_Click(object sender, RoutedEventArgs e) => Vm?.Stop();

    void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawChart();

    // ----- Gráfico: requests por intervalo + avg de response time -----

    void RedrawChart()
    {
        ChartCanvas.Children.Clear();
        var samples = Vm?.Samples;
        var hasData = samples is { Count: >= 2 };
        ChartHint.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        if (!hasData) return;

        var w = ChartCanvas.ActualWidth;
        var h = ChartCanvas.ActualHeight;
        if (w < 60 || h < 40) return;

        var total = Math.Max(samples![^1].T, 1);
        var bucketMs = Math.Max(200, total / 60.0);
        var n = Math.Max(2, (int)(total / bucketMs) + 1);

        var counts = new double[n];
        var sums = new double[n];
        foreach (var s in samples)
        {
            var i = Math.Min(n - 1, (int)(s.T / bucketMs));
            counts[i]++;
            sums[i] += s.Ms;
        }

        var avgs = new double[n];
        double previous = 0;
        for (var i = 0; i < n; i++)
        {
            if (counts[i] > 0) previous = sums[i] / counts[i];
            avgs[i] = previous; // los buckets vacíos arrastran el último promedio
        }

        var maxCount = Math.Max(counts.Max(), 1);
        var maxAvg = Math.Max(avgs.Max(), 1);

        double X(int i) => i / (double)(n - 1) * (w - 4) + 2;
        double Y(double v, double max) => h - 6 - v / max * (h - 26);

        var accent = (Brush)FindResource("AccentBrush");
        var peach = (Brush)FindResource("PeachBrush");
        var overlay = (Brush)FindResource("OverlayBrush");

        var countLine = new Polyline { Stroke = accent, StrokeThickness = 1.6 };
        var avgLine = new Polyline { Stroke = peach, StrokeThickness = 1.6 };
        for (var i = 0; i < n; i++)
        {
            countLine.Points.Add(new Point(X(i), Y(counts[i], maxCount)));
            avgLine.Points.Add(new Point(X(i), Y(avgs[i], maxAvg)));
        }
        ChartCanvas.Children.Add(countLine);
        ChartCanvas.Children.Add(avgLine);

        var countLabel = new TextBlock
        {
            Text = $"máx {maxCount:0} req / {bucketMs / 1000.0:0.#}s",
            FontSize = 10,
            Foreground = accent
        };
        Canvas.SetLeft(countLabel, 2);
        Canvas.SetTop(countLabel, 2);
        ChartCanvas.Children.Add(countLabel);

        var avgLabel = new TextBlock { Text = $"máx {maxAvg:0} ms", FontSize = 10, Foreground = peach };
        Canvas.SetLeft(avgLabel, 2);
        Canvas.SetTop(avgLabel, 16);
        ChartCanvas.Children.Add(avgLabel);

        var timeLabel = new TextBlock
        {
            Text = $"{total / 1000.0:0.#}s",
            FontSize = 10,
            Foreground = overlay
        };
        Canvas.SetRight(timeLabel, 2);
        Canvas.SetBottom(timeLabel, 0);
        ChartCanvas.Children.Add(timeLabel);
    }
}
