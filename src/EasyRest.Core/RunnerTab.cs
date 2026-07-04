using System.Collections.ObjectModel;
using System.Diagnostics;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest;

public class RunResult
{
    public int Iteration { get; set; }
    /// <summary>Usuario virtual (1..N) que ejecutó esta request.</summary>
    public int User { get; set; }
    public string Name { get; set; } = "";
    public string Method { get; set; } = "";
    public string Status { get; set; } = "";
    public long TimeMs { get; set; }
    public string Detail { get; set; } = "";
}

public class SamplePoint
{
    public double T { get; set; }    // ms desde el inicio de la corrida
    public double Ms { get; set; }   // duración de la request
    public bool Ok { get; set; }
}

/// <summary>Pestaña del runner: configuración, ejecución, resultados y estadísticas en vivo.</summary>
public class RunnerTab : Observable
{
    public const string AllRequestsOption = "(Todas las requests)";
    public const string NoEnvironment = "(Sin ambiente)";

    readonly ObservableCollection<EnvironmentModel> _environments;
    CancellationTokenSource? _cts;

    RequestCollection? _selectedCollection;
    object? _selectedRequestOption = AllRequestsOption;
    object? _selectedEnvOption = NoEnvironment;
    string _iterationsText = "1";
    string _virtualUsersText = "1";
    string _rampUpText = "0";
    string _durationText = "30";
    string _selectedMode = ModeIterations;
    string _delayText = "0";
    bool _stopOnError;
    bool _isRunning;
    double _progressValue;
    double _progressMax = 1;
    string _summary = "";
    string _avgText = "—", _p50Text = "—", _p95Text = "—", _p99Text = "—", _minText = "—", _maxText = "—";
    string _okText = "0", _failText = "0";
    string _peakRpsText = "—", _errorRateText = "—";

    public const string ModeIterations = "Iteraciones";
    public const string ModeDuration = "Duración (s)";

    public RunnerTab(ObservableCollection<RequestCollection> collections,
                     ObservableCollection<EnvironmentModel> environments,
                     EnvironmentModel? activeEnv)
    {
        Collections = collections;
        _environments = environments;

        RebuildEnvOptions();
        _environments.CollectionChanged += (_, _) => RebuildEnvOptions();

        SelectedCollection = collections.FirstOrDefault();
        SelectedEnvOption = activeEnv != null && _environments.Contains(activeEnv)
            ? activeEnv
            : NoEnvironment;
    }

    public ObservableCollection<RequestCollection> Collections { get; }
    public ObservableCollection<object> RequestOptions { get; } = new();
    public ObservableCollection<object> EnvOptions { get; } = new();
    public ObservableCollection<RunResult> Results { get; } = new();

    /// <summary>Muestras de la corrida en curso, para el gráfico temporal.</summary>
    public List<SamplePoint> Samples { get; } = new();

    /// <summary>Se dispara con cada resultado nuevo (para redibujar el gráfico y scrollear la grilla).</summary>
    public event Action? Updated;

    public RequestCollection? SelectedCollection
    {
        get => _selectedCollection;
        set
        {
            if (!Set(ref _selectedCollection, value)) return;
            RequestOptions.Clear();
            RequestOptions.Add(AllRequestsOption);
            if (value != null)
                foreach (var r in value.AllRequests) RequestOptions.Add(r);
            SelectedRequestOption = AllRequestsOption;
        }
    }

    public object? SelectedRequestOption
    {
        get => _selectedRequestOption;
        set => Set(ref _selectedRequestOption, value);
    }

    public object? SelectedEnvOption
    {
        get => _selectedEnvOption;
        set => Set(ref _selectedEnvOption, value);
    }

    public string IterationsText { get => _iterationsText; set => Set(ref _iterationsText, value); }

    /// <summary>Cantidad de usuarios virtuales que corren las iteraciones en simultáneo.</summary>
    public string VirtualUsersText { get => _virtualUsersText; set => Set(ref _virtualUsersText, value); }

    /// <summary>Segundos para escalonar el arranque de los usuarios (0 = todos juntos).</summary>
    public string RampUpText { get => _rampUpText; set => Set(ref _rampUpText, value); }

    /// <summary>Duración de la corrida en segundos (modo Duración).</summary>
    public string DurationText { get => _durationText; set => Set(ref _durationText, value); }

    public string[] Modes { get; } = { ModeIterations, ModeDuration };

    /// <summary>Modo de corrida: cantidad fija de iteraciones, o correr durante X segundos.</summary>
    public string SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!Set(ref _selectedMode, value)) return;
            Raise(nameof(IsIterationsMode));
            Raise(nameof(IsDurationMode));
        }
    }

    public bool IsIterationsMode => _selectedMode == ModeIterations;
    public bool IsDurationMode => _selectedMode == ModeDuration;

    public string DelayText { get => _delayText; set => Set(ref _delayText, value); }
    public bool StopOnError { get => _stopOnError; set => Set(ref _stopOnError, value); }

    public bool IsRunning
    {
        get => _isRunning;
        private set { if (Set(ref _isRunning, value)) Raise(nameof(IsNotRunning)); }
    }

    public bool IsNotRunning => !_isRunning;

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

    /// <summary>Máximo de requests completadas en un segundo durante la corrida.</summary>
    public string PeakRpsText { get => _peakRpsText; set => Set(ref _peakRpsText, value); }

    /// <summary>Porcentaje de requests fallidas.</summary>
    public string ErrorRateText { get => _errorRateText; set => Set(ref _errorRateText, value); }

    void RebuildEnvOptions()
    {
        var selected = SelectedEnvOption;
        EnvOptions.Clear();
        EnvOptions.Add(NoEnvironment);
        foreach (var e in _environments) EnvOptions.Add(e);
        SelectedEnvOption = selected is EnvironmentModel env && _environments.Contains(env)
            ? selected
            : NoEnvironment;
    }

    /// <summary>Selecciona la colección y la request indicadas (usado por "Ejecutar en Runner").</summary>
    public void PreselectRequest(RequestItem request)
    {
        var owner = Collections.FirstOrDefault(c => c.AllRequests.Contains(request));
        if (owner == null) return;
        SelectedCollection = owner;
        SelectedRequestOption = RequestOptions.OfType<RequestItem>().FirstOrDefault(r => r == request)
            ?? (object)AllRequestsOption;
    }

    public async Task RunAsync()
    {
        if (IsRunning) return;
        if (SelectedCollection is not { } col)
        {
            Summary = "Seleccioná una colección.";
            return;
        }

        var requests = SelectedRequestOption is RequestItem single
            ? new List<RequestItem> { single }
            : col.AllRequests.ToList();
        if (requests.Count == 0)
        {
            Summary = "La colección seleccionada no tiene requests.";
            return;
        }

        var useDuration = IsDurationMode;
        if (!int.TryParse(IterationsText, out var iterations) || iterations < 1) iterations = 1;
        if (!int.TryParse(DurationText, out var durationSec) || durationSec < 1) durationSec = 1;
        var durationMs = durationSec * 1000L;
        if (!int.TryParse(VirtualUsersText, out var users) || users < 1) users = 1;
        if (!int.TryParse(RampUpText, out var rampSec) || rampSec < 0) rampSec = 0;
        var rampMs = rampSec * 1000.0;
        int.TryParse(DelayText, out var delay);
        var env = SelectedEnvOption as EnvironmentModel;

        Results.Clear();
        Samples.Clear();
        var times = new List<double>();
        var perSecond = new Dictionary<long, int>();
        int ok = 0, failed = 0, peakRps = 0;
        PeakRpsText = "—";
        UpdateStats(times, ok, failed);

        _cts = new CancellationTokenSource();
        IsRunning = true;
        ProgressMax = useDuration ? durationMs : (double)users * iterations * requests.Count;
        ProgressValue = 0;
        Summary = "";
        var runWatch = Stopwatch.StartNew();

        // el estado compartido lo tocan varios usuarios virtuales a la vez → un candado.
        // (las continuaciones vuelven al hilo de UI, pero el lock lo deja explícito y seguro)
        var gate = new object();
        long lastRedraw = -1000;

        // un "usuario virtual": corre las iteraciones (o corre hasta que se cumple la duración)
        async Task RunUser(int userId)
        {
            // ramp-up: escalonar el arranque de cada usuario a lo largo del período configurado
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
                        ?? (testsTotal > 0
                            ? $"{testsPassed}/{testsTotal} tests OK"
                            : success ? "OK" : "Falló");

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

                        // req/s pico: requests completadas en cada segundo
                        var sec = elapsedMs / 1000;
                        var inSec = perSecond.GetValueOrDefault(sec) + 1;
                        perSecond[sec] = inSec;
                        if (inSec > peakRps) { peakRps = inSec; PeakRpsText = $"{peakRps}"; }

                        ProgressValue = useDuration ? Math.Min(durationMs, elapsedMs) : ProgressValue + 1;
                        UpdateStats(times, ok, failed);

                        var done = ok + failed;
                        var secs = elapsedMs / 1000.0;
                        var rps = secs > 0 ? done / secs : 0;
                        Summary = useDuration
                            ? $"{done} requests · {users} usuario(s) · {rps:0.#} req/s · {secs:0}s/{durationSec}s"
                            : $"{done}/{ProgressMax:0} requests · {users} usuario(s) · {rps:0.#} req/s";

                        // el redibujo del gráfico se limita a ~8 fps para no ahogar la UI con muchos VUs
                        redraw = elapsedMs - lastRedraw >= 120;
                        if (redraw) lastRedraw = elapsedMs;
                    }
                    if (redraw) Updated?.Invoke();

                    if (!success && StopOnError) { _cts.Cancel(); _cts.Token.ThrowIfCancellationRequested(); }
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
            IsRunning = false;
            Updated?.Invoke();
        }
    }

    public void Stop() => _cts?.Cancel();

    void UpdateStats(List<double> times, int ok, int failed)
    {
        OkText = ok.ToString();
        FailText = failed.ToString();
        var total = ok + failed;
        ErrorRateText = total == 0 ? "—" : $"{100.0 * failed / total:0.#}%";
        if (times.Count == 0)
        {
            AvgText = P50Text = P95Text = P99Text = MinText = MaxText = "—";
            return;
        }
        var sorted = times.OrderBy(t => t).ToList();
        AvgText = $"{times.Average():0} ms";
        P50Text = $"{Percentile(sorted, 0.50):0} ms";
        P95Text = $"{Percentile(sorted, 0.95):0} ms";
        P99Text = $"{Percentile(sorted, 0.99):0} ms";
        MinText = $"{sorted[0]:0} ms";
        MaxText = $"{sorted[^1]:0} ms";
    }

    static double Percentile(List<double> sorted, double p)
    {
        var index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
