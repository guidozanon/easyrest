using EasyRest.Models;

namespace EasyRest;

/// <summary>Configuración resuelta de una corrida, que arma el RunnerTab y ejecuta el RunTab.</summary>
public class RunConfig
{
    public required RequestCollection Collection { get; init; }
    public required List<RequestItem> Requests { get; init; }
    public EnvironmentModel? Env { get; init; }

    public string CollectionName { get; init; } = "";
    public string RequestLabel { get; init; } = "";
    public string EnvName { get; init; } = "";

    public bool UseDuration { get; init; }
    public int Iterations { get; init; } = 1;
    public int DurationSec { get; init; } = 30;
    public int Users { get; init; } = 1;
    public int RampSec { get; init; }
    public int Delay { get; init; }
    public bool StopOnError { get; init; }

    /// <summary>Descripción corta de la carga, para el título del tab y el registro.</summary>
    public string ModeLabel => UseDuration ? $"{DurationSec}s" : $"{Iterations} it";
    public string Summary => $"{RequestLabel} · {Users}u · {ModeLabel}"
        + (RampSec > 0 ? $" · ramp {RampSec}s" : "");
}

/// <summary>Configuración de runner guardada, para volver a correrla rápido.</summary>
public class RunnerPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string CollectionId { get; set; } = "";
    public string RequestId { get; set; } = "";   // vacío = todas las requests
    public string EnvId { get; set; } = "";        // vacío = sin ambiente
    public string Mode { get; set; } = "Iteraciones";
    public string Iterations { get; set; } = "1";
    public string Duration { get; set; } = "30";
    public string Users { get; set; } = "1";
    public string RampUp { get; set; } = "0";
    public string Delay { get; set; } = "0";
    public bool StopOnError { get; set; }

    public override string ToString() => Name;
}

/// <summary>Corrida guardada: config + métricas + muestras del gráfico + detalle por request.</summary>
public class RunRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SavedAt { get; set; } = "";
    public string Label { get; set; } = "";

    // config
    public string CollectionName { get; set; } = "";
    public string RequestLabel { get; set; } = "";
    public string EnvName { get; set; } = "";
    public string Mode { get; set; } = "";
    public int Users { get; set; }
    public int RampSec { get; set; }
    public int Delay { get; set; }

    // métricas
    public int Total { get; set; }
    public int Ok { get; set; }
    public int Failed { get; set; }
    public double ErrorRate { get; set; }
    public int PeakRps { get; set; }
    public double Avg { get; set; }
    public double P50 { get; set; }
    public double P95 { get; set; }
    public double P99 { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double WallSeconds { get; set; }

    // datos
    public List<SamplePoint> Samples { get; set; } = new();
    public List<RunResult> Results { get; set; } = new();
}
