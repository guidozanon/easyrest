using System.Collections.ObjectModel;
using System.Diagnostics;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest;

/// <summary>Una corrida del runner: ejecuta la carga configurada y muestra progreso,
/// métricas, gráfico y detalle en vivo. Se puede guardar como RunRecord al terminar.</summary>
public class RunTab : Observable
{
    readonly RunConfig _cfg;
    CancellationTokenSource? _cts;

    bool _isRunning;
    bool _isFinished;
    bool _isSaved;
    double _progressValue;
    double _progressMax = 1;
    string _summary = "";
    string _avgText = "—", _p50Text = "—", _p95Text = "—", _p99Text = "—", _minText = "—", _maxText = "—";
    string _okText = "0", _failText = "0", _peakRpsText = "—", _errorRateText = "—";

    // métricas numéricas (para guardar la corrida)
    double _avg, _p50, _p95, _p99, _min, _max, _errorRate, _wallSec;
    int _ok, _peakRps;

    public RunTab(RunConfig cfg)
    {
        _cfg = cfg;
        Title = cfg.Summary;
    }

    public RunConfig Config => _cfg;
    public string Title { get; }

    public ObservableCollection<RunResult> Results { get; } = new();
    public List<SamplePoint> Samples { get; } = new();

    /// <summary>Se dispara con cada avance (para redibujar el gráfico y scrollear la grilla).</summary>
    public event Action? Updated;

    public bool IsRunning
    {
        get => _isRunning;
        private set { if (Set(ref _isRunning, value)) Raise(nameof(IsNotRunning)); }
    }
    public bool IsNotRunning => !_isRunning;

    /// <summary>La corrida terminó (completada o detenida): habilita guardar.</summary>
    public bool IsFinished { get => _isFinished; private set { if (Set(ref _isFinished, value)) Raise(nameof(CanSave)); } }
    public bool IsSaved { get => _isSaved; private set { if (Set(ref _isSaved, value)) Raise(nameof(CanSave)); } }

    /// <summary>Se puede guardar: la corrida terminó y todavía no se guardó.</summary>
    public bool CanSave => _isFinished && !_isSaved;

    public double ProgressValue { get => _progressValue; set => Set(ref _progressValue, value); }
    public double ProgressMax { get => _progressMax; set => Set(ref _progressMax, value); }
    public string Summary { get => _summary; set => Set(ref _summary, value); }

    public string AvgText { get => _avgText; set => Set(ref _avgText, value); }
    public string P50Text { get => _p50Text; set => Set(ref _p50Text, value); }
    public string P95Text { get => _p95Text; set => Set(ref _p95Text, value); }
    public string P99Text { get => _p99Text; set => Set(ref _p99Text, value); }
    public string MinText { get => _minText; set => Set(ref _minText, value); }
    public string MaxText { get => _maxText; set => Set(ref _maxText, value); }
    public string OkText { get => _okText; set => Set(ref _okText, value); }
    public string FailText { get => _failText; set => Set(ref _failText, value); }
    public string PeakRpsText { get => _peakRpsText; set => Set(ref _peakRpsText, value); }
    public string ErrorRateText { get => _errorRateText; set => Set(ref _errorRateText, value); }

    public void Stop() => _cts?.Cancel();

    public async Task StartAsync()
    {
        if (IsRunning || IsFinished) return;

        var col = _cfg.Collection;
        var requests = _cfg.Requests;
        var env = _cfg.Env;
        var useDuration = _cfg.UseDuration;
        var iterations = Math.Max(1, _cfg.Iterations);
        var durationMs = Math.Max(1, _cfg.DurationSec) * 1000L;
        var users = Math.Max(1, _cfg.Users);
        var rampMs = Math.Max(0, _cfg.RampSec) * 1000.0;
        var delay = Math.Max(0, _cfg.Delay);

        var times = new List<double>();
        var perSecond = new Dictionary<long, int>();
        int ok = 0, failed = 0, peakRps = 0;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        ProgressMax = useDuration ? durationMs : (double)users * iterations * requests.Count;
        ProgressValue = 0;
        Summary = "";
        var runWatch = Stopwatch.StartNew();

        var gate = new object();
        long lastRedraw = -1000;

        async Task RunUser(int userId)
        {
            if (rampMs > 0 && userId > 1)
                await Task.Delay((int)(rampMs * (userId - 1) / users), _cts!.Token);

            var iteration = 0;
            while (true)
            {
                _cts!.Token.ThrowIfCancellationRequested();
                if (useDuration) { if (runWatch.ElapsedMilliseconds >= durationMs) break; }
                else if (iteration >= iterations) break;
                iteration++;

                foreach (var req in requests)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    if (useDuration && runWatch.ElapsedMilliseconds >= durationMs) break;

                    var r = await HttpExecutor.ExecuteAsync(req, col, env, _cts.Token);
                    var testsTotal = r.ScriptTests?.Count ?? 0;
                    var testsPassed = r.ScriptTests?.Count(t => t.Passed) ?? 0;
                    var success = r.Error == null && r.IsSuccess && testsPassed == testsTotal &&
                                  r.ScriptError == null;
                    var detail = r.Error
                        ?? r.ScriptError
                        ?? (testsTotal > 0 ? $"{testsPassed}/{testsTotal} tests OK" : success ? "OK" : "Falló");

                    bool redraw;
                    lock (gate)
                    {
                        if (success) ok++; else failed++;
                        times.Add(r.ElapsedMs);
                        var elapsedMs = runWatch.ElapsedMilliseconds;
                        Samples.Add(new SamplePoint { T = elapsedMs, Ms = r.ElapsedMs, Ok = success });
                        Results.Add(new RunResult
                        {
                            Iteration = iteration,
                            User = userId,
                            Name = req.Name,
                            Method = req.Method,
                            Status = r.Error != null ? "ERROR" : $"{r.StatusCode} {r.StatusText}",
                            TimeMs = r.ElapsedMs,
                            Detail = detail
                        });

                        var sec = elapsedMs / 1000;
                        var inSec = perSecond.GetValueOrDefault(sec) + 1;
                        perSecond[sec] = inSec;
                        if (inSec > peakRps) { peakRps = inSec; _peakRps = peakRps; PeakRpsText = $"{peakRps}"; }

                        ProgressValue = useDuration ? Math.Min(durationMs, elapsedMs) : ProgressValue + 1;
                        UpdateStats(times, ok, failed);
                        _wallSec = elapsedMs / 1000.0;

                        var done = ok + failed;
                        var rps = _wallSec > 0 ? done / _wallSec : 0;
                        Summary = useDuration
                            ? $"{done} requests · {users} usuario(s) · {rps:0.#} req/s · {_wallSec:0}s/{_cfg.DurationSec}s"
                            : $"{done}/{ProgressMax:0} requests · {users} usuario(s) · {rps:0.#} req/s";

                        redraw = elapsedMs - lastRedraw >= 120;
                        if (redraw) lastRedraw = elapsedMs;
                    }
                    if (redraw) Updated?.Invoke();

                    if (!success && _cfg.StopOnError) { _cts.Cancel(); _cts.Token.ThrowIfCancellationRequested(); }
                    if (delay > 0) await Task.Delay(delay, _cts.Token);
                }
            }
        }

        try
        {
            await Task.WhenAll(Enumerable.Range(1, users).Select(RunUser));
            ProgressValue = ProgressMax;
            Summary += " · completado";
        }
        catch (OperationCanceledException)
        {
            Summary += " · detenido";
        }
        finally
        {
            _wallSec = runWatch.ElapsedMilliseconds / 1000.0;
            IsRunning = false;
            IsFinished = true;
            Updated?.Invoke();
        }
    }

    /// <summary>Arma el registro persistible de esta corrida.</summary>
    public RunRecord ToRecord(string savedAt) => new()
    {
        SavedAt = savedAt,
        Label = _cfg.Summary,
        CollectionName = _cfg.CollectionName,
        RequestLabel = _cfg.RequestLabel,
        EnvName = _cfg.EnvName,
        Mode = _cfg.ModeLabel,
        Users = _cfg.Users,
        RampSec = _cfg.RampSec,
        Delay = _cfg.Delay,
        Total = _ok + (Results.Count - _ok),
        Ok = _ok,
        Failed = Results.Count - _ok,
        ErrorRate = _errorRate,
        PeakRps = _peakRps,
        Avg = _avg,
        P50 = _p50,
        P95 = _p95,
        P99 = _p99,
        Min = _min,
        Max = _max,
        WallSeconds = _wallSec,
        Samples = Samples.ToList(),
        Results = Results.ToList()
    };

    public void MarkSaved() => IsSaved = true;

    void UpdateStats(List<double> times, int ok, int failed)
    {
        _ok = ok;
        OkText = ok.ToString();
        FailText = failed.ToString();
        var total = ok + failed;
        _errorRate = total == 0 ? 0 : 100.0 * failed / total;
        ErrorRateText = total == 0 ? "—" : $"{_errorRate:0.#}%";
        if (times.Count == 0)
        {
            AvgText = P50Text = P95Text = P99Text = MinText = MaxText = "—";
            return;
        }
        var sorted = times.OrderBy(t => t).ToList();
        _avg = times.Average();
        _p50 = Percentile(sorted, 0.50);
        _p95 = Percentile(sorted, 0.95);
        _p99 = Percentile(sorted, 0.99);
        _min = sorted[0];
        _max = sorted[^1];
        AvgText = $"{_avg:0} ms";
        P50Text = $"{_p50:0} ms";
        P95Text = $"{_p95:0} ms";
        P99Text = $"{_p99:0} ms";
        MinText = $"{_min:0} ms";
        MaxText = $"{_max:0} ms";
    }

    static double Percentile(List<double> sorted, double p)
    {
        var index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
