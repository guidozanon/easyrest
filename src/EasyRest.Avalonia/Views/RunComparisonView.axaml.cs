using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest;

namespace EasyRest.Avalonia.Views;

public partial class RunComparisonView : UserControl
{
    RunComparisonTab? _hooked;

    static readonly Color[] SeriesColors =
    {
        Color.Parse("#89B4FA"), Color.Parse("#A6E3A1"), Color.Parse("#FAB387"),
        Color.Parse("#CBA6F7"), Color.Parse("#F9E2AF"), Color.Parse("#F38BA8"),
    };

    const double MarginL = 40, MarginR = 12, MarginT = 10, MarginB = 18;

    public RunComparisonView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rehook();
        Loaded += (_, _) => Refresh();
    }

    RunComparisonTab? Vm => DataContext as RunComparisonTab;

    void Rehook()
    {
        if (_hooked != null) _hooked.SelectionChanged -= Refresh;
        _hooked = Vm;
        if (_hooked != null) _hooked.SelectionChanged += Refresh;
        Refresh();
    }

    void Refresh()
    {
        var selected = Vm?.Selected ?? new();
        MetricsGrid.ItemsSource = selected;
        EmptyHint.IsVisible = selected.Count == 0;
        RedrawChart();
    }

    void Reload_Click(object? sender, RoutedEventArgs e) => Vm?.Reload();

    void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is ComparisonEntry entry) Vm?.Delete(entry);
    }

    void Chart_SizeChanged(object? sender, SizeChangedEventArgs e) => RedrawChart();

    IBrush Res(string key) => (IBrush)this.FindResource(key)!;

    void RedrawChart()
    {
        ChartCanvas.Children.Clear();
        Legend.Children.Clear();

        var runs = (Vm?.Selected ?? new()).Where(r => r.Samples.Count >= 2).ToList();
        var w = ChartCanvas.Bounds.Width;
        var h = ChartCanvas.Bounds.Height;
        if (runs.Count == 0 || w < 120 || h < 60) return;

        var maxT = runs.Max(r => r.Samples[^1].T);
        if (maxT <= 0) return;

        var bucketMs = Math.Clamp(maxT / 45.0, 400, 1500);
        var n = Math.Max(2, (int)Math.Ceiling(maxT / bucketMs));
        var bucketSec = bucketMs / 1000.0;

        // req/s por bucket para cada corrida
        var series = new List<double[]>();
        double maxReqs = 1;
        foreach (var run in runs)
        {
            var counts = new double[n];
            foreach (var s in run.Samples)
                counts[Math.Min(n - 1, (int)(s.T / bucketMs))]++;
            var reqs = new double[n];
            for (var i = 0; i < n; i++) reqs[i] = counts[i] / bucketSec;
            reqs = Smooth(reqs);
            maxReqs = Math.Max(maxReqs, reqs.Max());
            series.Add(reqs);
        }
        maxReqs = NiceCeil(maxReqs);

        var plotLeft = MarginL;
        var plotRight = w - MarginR;
        var plotW = plotRight - plotLeft;
        var plotBottom = h - MarginB;
        var plotH = plotBottom - MarginT;

        var overlay = Res("OverlayBrush");
        var grid = Res("Surface0");

        const int gridLines = 4;
        for (var g = 0; g <= gridLines; g++)
        {
            var y = MarginT + g / (double)gridLines * plotH;
            ChartCanvas.Children.Add(new Line
            {
                StartPoint = new Point(plotLeft, y), EndPoint = new Point(plotRight, y),
                Stroke = grid, StrokeThickness = 1
            });
            var val = maxReqs * (1 - g / (double)gridLines);
            AddText($"{val:0.#}", 2, y - 7, overlay, 9, MarginL - 6, TextAlignment.Right);
        }
        AddText("0s", plotLeft, plotBottom + 3, overlay, 9, 40, TextAlignment.Left);
        AddText($"{maxT / 1000.0:0.#}s", plotRight - 40, plotBottom + 3, overlay, 9, 40, TextAlignment.Right);

        for (var si = 0; si < series.Count; si++)
        {
            var color = SeriesColors[si % SeriesColors.Length];
            var reqs = series[si];
            var line = new Polyline { Stroke = new SolidColorBrush(color), StrokeThickness = 1.8 };
            for (var i = 0; i < n; i++)
            {
                var x = plotLeft + (n == 1 ? 0 : i / (double)(n - 1) * plotW);
                var y = plotBottom - reqs[i] / maxReqs * plotH;
                line.Points.Add(new Point(x, y));
            }
            ChartCanvas.Children.Add(line);

            // leyenda
            var swatch = new Rectangle { Width = 14, Height = 3, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = runs[si].Label, FontSize = 10, Foreground = Res("SubtextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            Legend.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, label } });
        }
    }

    void AddText(string text, double x, double y, IBrush brush, double size, double width, TextAlignment align)
    {
        var tb = new TextBlock { Text = text, FontSize = size, Foreground = brush, Width = width, TextAlignment = align };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        ChartCanvas.Children.Add(tb);
    }

    static double[] Smooth(double[] v)
    {
        if (v.Length < 3) return v;
        var r = new double[v.Length];
        for (var i = 0; i < v.Length; i++)
        {
            var a = Math.Max(0, i - 1);
            var b = Math.Min(v.Length - 1, i + 1);
            double sum = 0;
            for (var j = a; j <= b; j++) sum += v[j];
            r[i] = sum / (b - a + 1);
        }
        return r;
    }

    static double NiceCeil(double v)
    {
        if (v <= 0) return 1;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(v)));
        var norm = v / mag;
        var nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10;
        return nice * mag;
    }
}
