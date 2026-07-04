using System.Collections.ObjectModel;
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

/// <summary>Pestaña del runner: SOLO configuración. Al correr, arma un RunConfig y el host
/// abre un RunTab que ejecuta y muestra los resultados.</summary>
public class RunnerTab : Observable
{
    public const string AllRequestsOption = "(Todas las requests)";
    public const string NoEnvironment = "(Sin ambiente)";
    public const string ModeIterations = "Iteraciones";
    public const string ModeDuration = "Duración (s)";

    readonly ObservableCollection<EnvironmentModel> _environments;

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
    string _configError = "";

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

        LoadPresets();
    }

    public ObservableCollection<RequestCollection> Collections { get; }
    public ObservableCollection<object> RequestOptions { get; } = new();
    public ObservableCollection<object> EnvOptions { get; } = new();

    /// <summary>Presets de configuración guardados.</summary>
    public ObservableCollection<RunnerPreset> Presets { get; } = new();

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

    /// <summary>Mensaje de validación al intentar correr (vacío = todo OK).</summary>
    public string ConfigError { get => _configError; set => Set(ref _configError, value); }

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

    /// <summary>Valida la configuración y arma el RunConfig. Devuelve null (y setea ConfigError) si no se puede correr.</summary>
    public RunConfig? BuildConfig()
    {
        ConfigError = "";
        if (SelectedCollection is not { } col)
        {
            ConfigError = "Seleccioná una colección.";
            return null;
        }

        var single = SelectedRequestOption as RequestItem;
        var requests = single != null ? new List<RequestItem> { single } : col.AllRequests.ToList();
        if (requests.Count == 0)
        {
            ConfigError = "La colección seleccionada no tiene requests.";
            return null;
        }

        int.TryParse(IterationsText, out var iterations);
        int.TryParse(DurationText, out var durationSec);
        int.TryParse(VirtualUsersText, out var users);
        int.TryParse(RampUpText, out var rampSec);
        int.TryParse(DelayText, out var delay);

        var env = SelectedEnvOption as EnvironmentModel;
        return new RunConfig
        {
            Collection = col,
            Requests = requests,
            Env = env,
            CollectionName = col.Name,
            RequestLabel = single?.Name ?? AllRequestsOption,
            EnvName = env?.Name ?? NoEnvironment,
            UseDuration = IsDurationMode,
            Iterations = Math.Max(1, iterations),
            DurationSec = Math.Max(1, durationSec),
            Users = Math.Max(1, users),
            RampSec = Math.Max(0, rampSec),
            Delay = Math.Max(0, delay),
            StopOnError = StopOnError
        };
    }

    // ----- Presets -----

    void LoadPresets()
    {
        Presets.Clear();
        foreach (var p in Storage.LoadRunnerPresets()) Presets.Add(p);
    }

    /// <summary>Carga la config de un preset en los campos (resolviendo colección/request/ambiente por Id).</summary>
    public void ApplyPreset(RunnerPreset p)
    {
        var col = Collections.FirstOrDefault(c => c.Id == p.CollectionId);
        if (col != null) SelectedCollection = col;   // esto reconstruye RequestOptions

        SelectedRequestOption = string.IsNullOrEmpty(p.RequestId)
            ? AllRequestsOption
            : RequestOptions.OfType<RequestItem>().FirstOrDefault(r => r.Id == p.RequestId) ?? (object)AllRequestsOption;
        SelectedEnvOption = string.IsNullOrEmpty(p.EnvId)
            ? NoEnvironment
            : EnvOptions.OfType<EnvironmentModel>().FirstOrDefault(e => e.Id == p.EnvId) ?? (object)NoEnvironment;

        SelectedMode = p.Mode;
        IterationsText = p.Iterations;
        DurationText = p.Duration;
        VirtualUsersText = p.Users;
        RampUpText = p.RampUp;
        DelayText = p.Delay;
        StopOnError = p.StopOnError;
    }

    RunnerPreset BuildPreset(string name) => new()
    {
        Name = name.Trim(),
        CollectionId = SelectedCollection?.Id ?? "",
        RequestId = (SelectedRequestOption as RequestItem)?.Id ?? "",
        EnvId = (SelectedEnvOption as EnvironmentModel)?.Id ?? "",
        Mode = SelectedMode,
        Iterations = IterationsText,
        Duration = DurationText,
        Users = VirtualUsersText,
        RampUp = RampUpText,
        Delay = DelayText,
        StopOnError = StopOnError
    };

    /// <summary>Guarda la config actual como preset (pisa uno con el mismo nombre). Devuelve el preset.</summary>
    public RunnerPreset? SavePreset(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return null;
        var preset = BuildPreset(trimmed);
        var existing = Presets.FirstOrDefault(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { preset.Id = existing.Id; Presets.Remove(existing); }
        Presets.Add(preset);
        Storage.SaveRunnerPresets(Presets);
        return preset;
    }

    public void DeletePreset(RunnerPreset p)
    {
        Presets.Remove(p);
        Storage.SaveRunnerPresets(Presets);
    }
}
