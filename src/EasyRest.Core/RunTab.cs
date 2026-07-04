using System.Collections.ObjectModel;
using System.Diagnostics;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest;

/// <summary>Una corrida del runner: ejecuta la carga configurada y muestra progreso,
/// métricas, gráfico y detalle en vivo. Se puede guardar como RunRecord al terminar.
///
/// Concurrencia: los usuarios virtuales corren la red en background (ConfigureAwait(false)),
/// acumulan resultados bajo un lock y refrescan la UI en lote (~7 fps) sobre el
/// SynchronizationContext de la UI. Así ni la grilla ni el gráfico se tocan desde otro hilo
/// ni se satura el hilo de UI, que era lo que crasheaba el compositor al pasar el mouse.</summary>
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
    int _ok, _failed, _peakRps;

    public RunTab(RunConfig cfg)
    {
        _cfg = cfg;
        Title = cfg.Summary;
    }

    public RunConfig Config => _cfg;
    public string Title { get; }

    public ObservableCollection<RunResult> Results { get; } = new();
    public List<SamplePoint> Samples { get; } = new();

    /// <summary>Se dispara (en el hilo de UI) con cada refresco en lote.</summary>
    public event Action? Updated;

    public bool IsRunning
    {
        get => _isRunning;
        private set { if (Set(ref _isRunning, value)) Raise(nameof(IsNotRunning)); }
    }
    public bool IsNotRunning => !_isRunning;

    public bool IsFinished { get => _isFinished; private set { if (Set(ref _isFinished, value)) Raise(nameof(CanSave)); } }
    public bool IsSaved { get => _isSaved; private set { if (Set(ref _isSaved, value)) Raise(nameof(CanSave)); } }
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

        // contexto de la UI: acá volcamos los refrescos en lote
        var ui = SynchronizationContext.Current;
        void PostToUi(Action a) { if (ui != null) ui.Post(_ => a(), null); else a(); }

        // estado compartido entre los usuarios virtuales (varios hilos del pool) → lock
        var gate = new object();
        var times = new List<double>();
        var perSecond = new Dictionary<long, int>();
        var pendingResults = new List<RunResult>();
        var pendingSamples = new List<SamplePoint>();
        int ok = 0, failed = 0, peakRps = 0;
        long lastPost = -10000;
        var runWatch = Stopwatch.StartNew();

        _cts = new CancellationTokenSource();
        IsRunning = true;
        ProgressMax = useDuration ? durationMs : (double)users * iterations * requests.Count;
        ProgressValue = 0;
        Summary = "";

        // refresco de UI (corre SIEMPRE en el hilo de UI): vuelca lo pendiente y actualiza props
        void Publish()
        {
            List<RunResult> rs; List<SamplePoint> ss; long elapsed;
            double avg, p50, p95, p99, min, max, errRate; int okS, failS, peakS, total;
            lock (gate)
            {
                rs = pendingResults; pendingResults = new();
                ss = pendingSamples; pendingSamples = new();
                elapsed = runWatch.ElapsedMilliseconds;
                okS = ok; failS = failed; peakS = peakRps; total = ok + failed;
                if (times.Count > 0)
                {
                    var sorted = times.OrderBy(t => t).ToList();
                    avg = times.Average();
                    p50 = Percentile(sorted, 0.50); p95 = Percentile(sorted, 0.95); p99 = Percentile(sorted, 0.99);
                    min = sorted[0]; max = sorted[^1];
                }
                else { avg = p50 = p95 = p99 = min = max = 0; }
                errRate = total == 0 ? 0 : 100.0 * failS / total;
                _ok = okS; _failed = failS; _peakRps = peakS;
                _avg = avg; _p50 = p50; _p95 = p95; _p99 = p99; _min = min; _max = max;
                _errorRate = errRate; _wallSec = elapsed / 1000.0;
            }

            foreach (var r in rs) Results.Add(r);
            Samples.AddRange(ss);

            OkText = okS.ToString();
            FailText = failS.ToString();
            ErrorRateText = total == 0 ? "—" : $"{errRate:0.#}%";
            PeakRpsText = peakS == 0 ? "—" : peakS.ToString();
            if (times.Count > 0 || total > 0)
            {
                AvgText = $"{avg:0} ms"; P50Text = $"{p50:0} ms"; P95Text = $"{p95:0} ms";
                P99Text = $"{p99:0} ms"; MinText = $"{min:0} ms"; MaxText = $"{max:0} ms";
            }
            ProgressValue = useDuration ? Math.Min(durationMs, elapsed) : total;
            var secs = elapsed / 1000.0;
            var rps = secs > 0 ? total / secs : 0;
            Summary = useDuration
                ? $"{total} requests · {users} usuario(s) · {rps:0.#} req/s · {secs:0}s/{_cfg.DurationSec}s"
                : $"{total}/{ProgressMax:0} requests · {users} usuario(s) · {rps:0.#} req/s";
            Updated?.Invoke();
        }

        async Task RunUser(int userId)
        {
            if (rampMs > 0 && userId > 1)
                await Task.Delay((int)(rampMs * (userId - 1) / users), _cts!.Token).ConfigureAwait(false);

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

                    // la red corre en background; el resultado NO vuelve al hilo de UI
                    var r = await HttpExecutor.ExecuteAsync(req, col, env, _cts.Token).ConfigureAwait(false);
                    var testsTotal = r.ScriptTests?.Count ?? 0;
                    var testsPassed = r.ScriptTests?.Count(t => t.Passed) ?? 0;
                    var success = r.Error == null && r.IsSuccess && testsPassed == testsTotal && r.ScriptError == null;
                    var detail = r.Error ?? r.ScriptError
                        ?? (testsTotal > 0 ? $"{testsPassed}/{testsTotal} tests OK" : success ? "OK" : "Falló");

                    bool post;
                    lock (gate)
                    {
                        if (success) ok++; else failed++;
                        times.Add(r.ElapsedMs);
                        var elapsedMs = runWatch.ElapsedMilliseconds;
                        pendingSamples.Add(new SamplePoint { T = elapsedMs, Ms = r.ElapsedMs, Ok = success });
                        pendingResults.Add(new RunResult
                        {
                            Iteration = iteration, User = userId, Name = req.Name, Method = req.Method,
                            Status = r.Error != null ? "ERROR" : $"{r.StatusCode} {r.StatusText}",
                            TimeMs = r.ElapsedMs, Detail = detail
                        });
                        var sec = elapsedMs / 1000;
                        var inSec = perSecond.GetValueOrDefault(sec) + 1;
                        perSecond[sec] = inSec;
                        if (inSec > peakRps) peakRps = inSec;

                        // limitar el refresco de UI a ~7 fps (independiente de cuántas requests/seg haya)
                        post = elapsedMs - lastPost >= 150;
                        if (post) lastPost = elapsedMs;
                    }
                    if (post) PostToUi(Publish);

                    if (!success && _cfg.StopOnError) { _cts.Cancel(); _cts.Token.ThrowIfCancellationRequested(); }
                    if (delay > 0) await Task.Delay(delay, _cts.Token).ConfigureAwait(false);
                }
            }
        }

        var completed = false;
        try
        {
            await Task.WhenAll(Enumerable.Range(1, users).Select(RunUser)).ConfigureAwait(false);
            completed = true;
        }
        catch (OperationCanceledException) { /* detenido o frenado por error */ }
        finally
        {
            // refresco final + banderas de fin, todo en el hilo de UI
            PostToUi(() =>
            {
                Publish();
                if (completed) { ProgressValue = ProgressMax; Summary += " · completado"; }
                else Summary += " · detenido";
                IsRunning = false;
                IsFinished = true;
                Updated?.Invoke();
            });
        }
    }

    public RunRecord ToRecord(string savedAt) => new()
    {
        SavedAt = savedAt, Label = _cfg.Summary,
        CollectionName = _cfg.CollectionName, RequestLabel = _cfg.RequestLabel, EnvName = _cfg.EnvName,
        Mode = _cfg.ModeLabel, Users = _cfg.Users, RampSec = _cfg.RampSec, Delay = _cfg.Delay,
        Total = _ok + _failed, Ok = _ok, Failed = _failed, ErrorRate = _errorRate, PeakRps = _peakRps,
        Avg = _avg, P50 = _p50, P95 = _p95, P99 = _p99, Min = _min, Max = _max, WallSeconds = _wallSec,
        Samples = Samples.ToList(), Results = Results.ToList()
    };

    public void MarkSaved() => IsSaved = true;

    static double Percentile(List<double> sorted, double p)
    {
        var index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
