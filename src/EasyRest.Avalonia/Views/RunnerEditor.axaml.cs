using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace EasyRest.Avalonia.Views;

public partial class RunnerEditor : UserControl
{
    RunnerTab? _hooked;

    public RunnerEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rehook();
        Loaded += (_, _) => RedrawChart();
    }

    RunnerTab? Vm => DataContext as RunnerTab;

    void Rehook()
    {
        if (_hooked != null) _hooked.Updated -= OnUpdated;
        _hooked = Vm;
        if (_hooked != null) _hooked.Updated += OnUpdated;
        RedrawChart();
    }

    void OnUpdated()
    {
        RedrawChart();
        if (Vm is { Results.Count: > 0 } vm) ResultsGrid.ScrollIntoView(vm.Results[^1], null);
    }

    async void Run_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.RunAsync();
    }

    void Stop_Click(object? sender, RoutedEventArgs e) => Vm?.Stop();

    void Chart_SizeChanged(object? sender, SizeChangedEventArgs e) => RedrawChart();

    void RedrawChart()
    {
        ChartCanvas.Children.Clear();
        var samples = Vm?.Samples;
        var hasData = samples is { Count: >= 2 };
        ChartHint.IsVisible = !hasData;
        if (!hasData) return;

        var w = ChartCanvas.Bounds.Width;
        var h = ChartCanvas.Bounds.Height;
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
            avgs[i] = previous;
        }

        var maxCount = Math.Max(counts.Max(), 1);
        var maxAvg = Math.Max(avgs.Max(), 1);

        double X(int i) => i / (double)(n - 1) * (w - 4) + 2;
        double Y(double v, double max) => h - 6 - v / max * (h - 26);

        var accent = (IBrush)this.FindResource("AccentBrush")!;
        var peach = (IBrush)this.FindResource("PeachBrush")!;
        var overlay = (IBrush)this.FindResource("OverlayBrush")!;

        var countLine = new Polyline { Stroke = accent, StrokeThickness = 1.6 };
        var avgLine = new Polyline { Stroke = peach, StrokeThickness = 1.6 };
        for (var i = 0; i < n; i++)
        {
            countLine.Points.Add(new Point(X(i), Y(counts[i], maxCount)));
            avgLine.Points.Add(new Point(X(i), Y(avgs[i], maxAvg)));
        }
        ChartCanvas.Children.Add(countLine);
        ChartCanvas.Children.Add(avgLine);

        AddLabel($"máx {maxCount:0} req / {bucketMs / 1000.0:0.#}s", 2, 2, accent);
        AddLabel($"máx {maxAvg:0} ms", 2, 16, peach);
        var timeLabel = MakeLabel($"{total / 1000.0:0.#}s", overlay);
        Canvas.SetLeft(timeLabel, w - 40);
        Canvas.SetTop(timeLabel, h - 16);
        ChartCanvas.Children.Add(timeLabel);
    }

    void AddLabel(string text, double x, double y, IBrush brush)
    {
        var tb = MakeLabel(text, brush);
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        ChartCanvas.Children.Add(tb);
    }

    static TextBlock MakeLabel(string text, IBrush brush) =>
        new() { Text = text, FontSize = 10, Foreground = brush };
}
