using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace EasyRest.Avalonia.Views;

public partial class RunnerEditor : UserControl
{
    RunnerTab? _hooked;

    // puntos del gráfico (coords de pantalla + valores) para el tooltip al pasar el mouse
    readonly List<(double X, double Reqs, double Ms, double TSec)> _points = new();
    double _plotLeft, _plotRight;
    Line? _crosshair;
    Border? _tooltip;
    TextBlock? _tipText;

    const double MarginL = 44, MarginR = 46, MarginT = 12, MarginB = 20;

    public RunnerEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rehook();
        Loaded += (_, _) => RedrawChart();
        ChartCanvas.PointerMoved += Chart_PointerMoved;
        ChartCanvas.PointerExited += (_, _) => HideTooltip();
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

    IBrush Res(string key) => (IBrush)this.FindResource(key)!;

    void RedrawChart()
    {
        ChartCanvas.Children.Clear();
        _points.Clear();
        _crosshair = null; _tooltip = null;

        var samples = Vm?.Samples;
        var hasData = samples is { Count: >= 2 };
        ChartHint.IsVisible = !hasData;
        if (!hasData) return;

        var w = ChartCanvas.Bounds.Width;
        var h = ChartCanvas.Bounds.Height;
        if (w < 120 || h < 80) return;

        var totalMs = Math.Max(samples![^1].T, 1);

        // buckets ~ 45 puntos, cada uno de al menos 400ms para que req/s sea estable
        var bucketMs = Math.Clamp(totalMs / 45.0, 400, 1500);
        var n = Math.Max(2, (int)Math.Ceiling(totalMs / bucketMs));
        var bucketSec = bucketMs / 1000.0;

        var counts = new double[n];
        var sums = new double[n];
        foreach (var s in samples)
        {
            var i = Math.Min(n - 1, (int)(s.T / bucketMs));
            counts[i]++;
            sums[i] += s.Ms;
        }

        // req/s por bucket y avg (arrastra el último si el bucket quedó vacío)
        var reqs = new double[n];
        var avgs = new double[n];
        double lastAvg = 0;
        for (var i = 0; i < n; i++)
        {
            reqs[i] = counts[i] / bucketSec;
            if (counts[i] > 0) lastAvg = sums[i] / counts[i];
            avgs[i] = lastAvg;
        }
        reqs = Smooth(reqs);
        avgs = Smooth(avgs);

        var maxReqs = Math.Max(Math.Ceiling(reqs.Max()), 1);
        var maxMs = Math.Max(avgs.Max(), 1);
        // redondear el máximo de ms a un valor "lindo"
        maxMs = NiceCeil(maxMs);

        _plotLeft = MarginL;
        _plotRight = w - MarginR;
        var plotW = _plotRight - _plotLeft;
        var plotBottom = h - MarginB;
        var plotH = plotBottom - MarginT;

        double X(int i) => _plotLeft + (n == 1 ? 0 : i / (double)(n - 1) * plotW);
        double YReq(double v) => plotBottom - v / maxReqs * plotH;
        double YMs(double v) => plotBottom - v / maxMs * plotH;

        var accent = Res("AccentBrush");
        var peach = Res("PeachBrush");
        var overlay = Res("OverlayBrush");
        var grid = Res("Surface0");

        // grilla horizontal + etiquetas de ejes (izq req/s, der ms)
        const int gridLines = 4;
        for (var g = 0; g <= gridLines; g++)
        {
            var y = MarginT + g / (double)gridLines * plotH;
            ChartCanvas.Children.Add(new Line
            {
                StartPoint = new Point(_plotLeft, y),
                EndPoint = new Point(_plotRight, y),
                Stroke = grid,
                StrokeThickness = 1
            });
            var reqVal = maxReqs * (1 - g / (double)gridLines);
            var msVal = maxMs * (1 - g / (double)gridLines);
            AddText($"{reqVal:0.#}", 2, y - 7, accent, 9, MarginL - 6, TextAlignment.Right);
            AddText($"{msVal:0}", _plotRight + 4, y - 7, peach, 9, MarginR - 6, TextAlignment.Left);
        }

        // etiquetas de tiempo en X (0, mitad, fin)
        AddText("0s", _plotLeft, plotBottom + 3, overlay, 9, 40, TextAlignment.Left);
        AddText($"{totalMs / 2000.0:0.#}s", _plotLeft + plotW / 2 - 20, plotBottom + 3, overlay, 9, 40, TextAlignment.Center);
        AddText($"{totalMs / 1000.0:0.#}s", _plotRight - 40, plotBottom + 3, overlay, 9, 40, TextAlignment.Right);

        // relleno + línea de avg ms (peach)
        var msFill = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(28, 0xFA, 0xB3, 0x87)) };
        var msLine = new Polyline { Stroke = peach, StrokeThickness = 1.8 };
        // relleno + línea de req/s (accent)
        var reqFill = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(30, 0x89, 0xB4, 0xFA)) };
        var reqLine = new Polyline { Stroke = accent, StrokeThickness = 1.8 };

        for (var i = 0; i < n; i++)
        {
            var x = X(i);
            msLine.Points.Add(new Point(x, YMs(avgs[i])));
            reqLine.Points.Add(new Point(x, YReq(reqs[i])));
            _points.Add((x, reqs[i], avgs[i], samples[Math.Min(samples.Count - 1, (int)(i * bucketMs / Math.Max(1, totalMs) * (samples.Count - 1)))].T / 1000.0));
        }
        BuildFill(msFill, msLine, plotBottom);
        BuildFill(reqFill, reqLine, plotBottom);

        ChartCanvas.Children.Add(msFill);
        ChartCanvas.Children.Add(reqFill);
        ChartCanvas.Children.Add(msLine);
        ChartCanvas.Children.Add(reqLine);

        // crosshair + tooltip (ocultos hasta el hover)
        _crosshair = new Line
        {
            Stroke = overlay, StrokeThickness = 1, IsVisible = false,
            StrokeDashArray = new AvaloniaList<double> { 3, 3 }
        };
        ChartCanvas.Children.Add(_crosshair);

        _tipText = new TextBlock { FontSize = 11, Foreground = Res("TextBrush") };
        _tooltip = new Border
        {
            Background = Res("PanelBrush"),
            BorderBrush = grid,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 5),
            IsVisible = false,
            Child = _tipText
        };
        ChartCanvas.Children.Add(_tooltip);
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

    static void BuildFill(Polygon fill, Polyline line, double baseline)
    {
        if (line.Points.Count == 0) return;
        foreach (var p in line.Points) fill.Points.Add(p);
        fill.Points.Add(new Point(line.Points[^1].X, baseline));
        fill.Points.Add(new Point(line.Points[0].X, baseline));
    }

    void AddText(string text, double x, double y, IBrush brush, double size, double width, TextAlignment align)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = size, Foreground = brush,
            Width = width, TextAlignment = align
        };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        ChartCanvas.Children.Add(tb);
    }

    // ----- Tooltip al pasar el mouse -----

    void Chart_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_points.Count == 0 || _crosshair == null || _tooltip == null || _tipText == null) return;
        var pos = e.GetPosition(ChartCanvas);
        if (pos.X < _plotLeft || pos.X > _plotRight) { HideTooltip(); return; }

        // punto más cercano en X
        var best = _points[0];
        var bestD = double.MaxValue;
        foreach (var p in _points)
        {
            var d = Math.Abs(p.X - pos.X);
            if (d < bestD) { bestD = d; best = p; }
        }

        _crosshair.StartPoint = new Point(best.X, MarginT);
        _crosshair.EndPoint = new Point(best.X, ChartCanvas.Bounds.Height - MarginB);
        _crosshair.IsVisible = true;

        _tipText.Text = $"{best.TSec:0.#}s\n{best.Reqs:0.#} req/s\n{best.Ms:0} ms";
        _tooltip.IsVisible = true;
        _tooltip.Measure(Size.Infinity);
        var tw = _tooltip.DesiredSize.Width;
        var tx = best.X + 12 + tw > _plotRight ? best.X - 12 - tw : best.X + 12;
        Canvas.SetLeft(_tooltip, tx);
        Canvas.SetTop(_tooltip, MarginT + 4);
    }

    void HideTooltip()
    {
        if (_crosshair != null) _crosshair.IsVisible = false;
        if (_tooltip != null) _tooltip.IsVisible = false;
    }
}
