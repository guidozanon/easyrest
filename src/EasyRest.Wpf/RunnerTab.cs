using System.Collections.ObjectModel;
using System.Diagnostics;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest;

public class RunResult
{
    public int Iteration { get; set; }
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
    string _delayText = "0";
    bool _stopOnError;
    bool _isRunning;
    double _progressValue;
    double _progressMax = 1;
    string _summary = "";
    string _avgText = "—", _p50Text = "—", _p95Text = "—", _p99Text = "—", _minText = "—", _maxText = "—";
    string _okText = "0", _failText = "0";

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

        if (!int.TryParse(IterationsText, out var iterations) || iterations < 1) iterations = 1;
        int.TryParse(DelayText, out var delay);
        var env = SelectedEnvOption as EnvironmentModel;

        Results.Clear();
        Samples.Clear();
        var times = new List<double>();
        int ok = 0, failed = 0;
        UpdateStats(times, ok, failed);

        _cts = new CancellationTokenSource();
        IsRunning = true;
        ProgressMax = iterations * requests.Count;
        ProgressValue = 0;
        Summary = "";
        var runWatch = Stopwatch.StartNew();

        try
        {
            for (var iteration = 1; iteration <= iterations; iteration++)
            {
                foreach (var req in requests)
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    var r = await HttpExecutor.ExecuteAsync(req, col, env, _cts.Token);
                    var testsTotal = r.ScriptTests?.Count ?? 0;
                    var testsPassed = r.ScriptTests?.Count(t => t.Passed) ?? 0;
                    var success = r.Error == null && r.IsSuccess && testsPassed == testsTotal &&
                                  r.ScriptError == null;
                    if (success) ok++; else failed++;
                    times.Add(r.ElapsedMs);
                    Samples.Add(new SamplePoint { T = runWatch.ElapsedMilliseconds, Ms = r.ElapsedMs, Ok = success });

                    var detail = r.Error
                        ?? r.ScriptError
                        ?? (testsTotal > 0
                            ? $"{testsPassed}/{testsTotal} tests OK"
                            : success ? "OK" : "Falló");

                    Results.Add(new RunResult
                    {
                        Iteration = iteration,
                        Name = req.Name,
                        Method = req.Method,
                        Status = r.Error != null ? "ERROR" : $"{r.StatusCode} {r.StatusText}",
                        TimeMs = r.ElapsedMs,
                        Detail = detail
                    });
                    ProgressValue++;
                    UpdateStats(times, ok, failed);
                    Summary = $"{ok + failed}/{ProgressMax:0} requests";
                    Updated?.Invoke();

                    if (!success && StopOnError) throw new OperationCanceledException();
                    if (delay > 0) await Task.Delay(delay, _cts.Token);
                }
            }
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
