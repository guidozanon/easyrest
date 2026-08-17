using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using EasyRest.Models;
using EasyRest.Services;

using Button = Avalonia.Controls.Button;
using Orientation = Avalonia.Layout.Orientation;

namespace EasyRest.Android;

/// <summary>Simulación de carga desde el teléfono: usuarios virtuales, iteraciones o duración, y
/// las métricas en vivo.
///
/// El motor es el <see cref="RunTab"/> del Core, el mismo que usa el escritorio: acá sólo hay
/// pantalla. Lo que no viene es el gráfico temporal ni la comparación de corridas — en una
/// pantalla de teléfono un gráfico de req/s es decorado, y comparar dos corridas se hace sentado.
/// Las corridas se pueden guardar, así que quedan para comparar después desde el escritorio.
///
/// Un aviso que conviene tener presente: correr carga desde un teléfono mide el teléfono y su
/// red tanto como al servidor. Sirve para ver si algo responde bien desde afuera, no para sacar
/// números de capacidad.</summary>
internal class RunnerView : UserControl
{
    readonly RequestCollection _colección;
    readonly List<RequestItem> _requests;
    readonly string _etiqueta;
    readonly Func<EnvironmentModel?> _ambiente;

    readonly TextBox _usuarios = Ui.Campo("1", "usuarios");
    readonly TextBox _cantidad = Ui.Campo("10", "iteraciones");
    readonly TextBox _rampa = Ui.Campo("0", "ramp-up (s)");
    readonly TextBox _demora = Ui.Campo("0", "delay (ms)");
    readonly CheckBox _frenar = new() { Content = "Frenar al primer error", MinHeight = Ui.Toque };

    readonly StackPanel _configuración = new() { Spacing = 10 };
    readonly StackPanel _métricas = new() { Spacing = 6 };
    readonly TextBlock _resumen = Ui.Parrafo("", Ui.Tenue, 12);
    readonly ProgressBar _progreso = new() { Height = 6, Minimum = 0, Maximum = 1 };
    readonly Button _correr;
    readonly Button _frenarBoton;
    readonly Button _guardar;

    bool _porDuración;
    RunTab? _corrida;

    public RunnerView(RequestCollection colección, List<RequestItem> requests, string etiqueta,
        Func<EnvironmentModel?> ambiente)
    {
        _colección = colección;
        _requests = requests;
        _etiqueta = etiqueta;
        _ambiente = ambiente;

        _correr = Ui.AccionAsync("Correr", CorrerAsync);
        _correr.Background = Ui.Acento;
        _correr.Foreground = Ui.Fondo;

        _frenarBoton = Ui.Accion("Frenar", () => _corrida?.Stop());
        _frenarBoton.IsEnabled = false;

        _guardar = Ui.Accion("Guardar corrida", Guardar);
        _guardar.IsEnabled = false;

        ArmarConfiguración();

        var pila = new StackPanel
        {
            Margin = new Thickness(12, 0, 12, 16),
            Spacing = 12,
            Children =
            {
                Ui.Rotulo($"{_etiqueta} · {_colección.Name}"),
                Ui.Tarjeta(_configuración),
                Ui.Barra(_correr, _frenarBoton, _guardar),
                _progreso,
                _resumen,
                Ui.Tarjeta(_métricas)
            }
        };

        MostrarMétricas();
        Content = new ScrollViewer { Content = pila };
    }

    void ArmarConfiguración()
    {
        _configuración.Children.Clear();

        _configuración.Children.Add(Ui.Rotulo("Usuarios virtuales (en simultáneo)"));
        _configuración.Children.Add(_usuarios);

        _configuración.Children.Add(Ui.Barra(
            Ui.Opcion("Iteraciones", !_porDuración, () => { _porDuración = false; ArmarConfiguración(); }),
            Ui.Opcion("Duración (s)", _porDuración, () => { _porDuración = true; ArmarConfiguración(); })));
        _configuración.Children.Add(_cantidad);

        _configuración.Children.Add(Ui.Rotulo("Ramp-up en segundos (arranque escalonado)"));
        _configuración.Children.Add(_rampa);
        _configuración.Children.Add(Ui.Rotulo("Delay entre requests, en ms"));
        _configuración.Children.Add(_demora);
        _configuración.Children.Add(_frenar);
    }

    static int Número(TextBox campo, int porDefecto) =>
        int.TryParse((campo.Text ?? "").Trim(), out var valor) && valor > 0 ? valor : porDefecto;

    async Task CorrerAsync()
    {
        if (_requests.Count == 0)
        {
            _resumen.Text = "No hay requests para correr.";
            return;
        }

        var ambiente = _ambiente();
        var cantidad = Número(_cantidad, _porDuración ? 30 : 10);

        var configuración = new RunConfig
        {
            Collection = _colección,
            Requests = _requests,
            Env = ambiente,
            CollectionName = _colección.Name,
            RequestLabel = _etiqueta,
            EnvName = ambiente?.Name ?? "(sin ambiente)",
            UseDuration = _porDuración,
            Iterations = _porDuración ? 1 : cantidad,
            DurationSec = _porDuración ? cantidad : 30,
            Users = Número(_usuarios, 1),
            RampSec = Número(_rampa, 0),
            Delay = Número(_demora, 0),
            StopOnError = _frenar.IsChecked == true
        };

        _corrida = new RunTab(configuración);
        // el motor avisa desde los hilos de los usuarios virtuales, no desde el de la UI
        _corrida.Updated += () => Dispatcher.UIThread.Post(MostrarMétricas);

        _correr.IsEnabled = false;
        _frenarBoton.IsEnabled = true;
        _guardar.IsEnabled = false;

        try
        {
            await _corrida.StartAsync();
        }
        catch (Exception ex)
        {
            _resumen.Text = $"La corrida falló: {ex.Message}";
        }
        finally
        {
            _correr.IsEnabled = true;
            _frenarBoton.IsEnabled = false;
            _guardar.IsEnabled = _corrida?.CanSave == true;
            MostrarMétricas();
        }
    }

    void MostrarMétricas()
    {
        _métricas.Children.Clear();

        if (_corrida == null)
        {
            _métricas.Children.Add(Ui.Rotulo("Todavía no corriste nada."));
            return;
        }

        _resumen.Text = _corrida.Summary;
        _progreso.Maximum = Math.Max(1, _corrida.ProgressMax);
        _progreso.Value = _corrida.ProgressValue;

        _métricas.Children.Add(Fila("Exitosas / fallidas", $"{_corrida.OkText} / {_corrida.FailText}"));
        _métricas.Children.Add(Fila("Tasa de error", _corrida.ErrorRateText));
        _métricas.Children.Add(Fila("req/s pico", _corrida.PeakRpsText));
        _métricas.Children.Add(Fila("Promedio", _corrida.AvgText));
        _métricas.Children.Add(Fila("p50 / p95 / p99",
            $"{_corrida.P50Text} / {_corrida.P95Text} / {_corrida.P99Text}"));
        _métricas.Children.Add(Fila("Mín / máx", $"{_corrida.MinText} / {_corrida.MaxText}"));

        // las últimas, que es lo que se mira cuando algo empieza a fallar
        var últimas = _corrida.Results.Skip(Math.Max(0, _corrida.Results.Count - 8)).ToList();
        if (últimas.Count > 0)
        {
            _métricas.Children.Add(Ui.Rotulo("Últimas"));
            foreach (var resultado in últimas)
                _métricas.Children.Add(Ui.Parrafo(
                    $"#{resultado.Iteration} u{resultado.User} · {resultado.Method} {resultado.Name} · " +
                    $"{resultado.Status} · {resultado.TimeMs} ms",
                    resultado.Status.StartsWith('2') ? Ui.Tenue : Ui.Rojo, 11));
        }
    }

    static Control Fila(string nombre, string valor) => new Grid
    {
        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        Children =
        {
            Ui.Rotulo(nombre),
            new TextBlock
            {
                Text = valor,
                FontSize = 13,
                Foreground = Ui.Normal,
                HorizontalAlignment = HorizontalAlignment.Right,
                [Grid.ColumnProperty] = 1
            }
        }
    };

    void Guardar()
    {
        if (_corrida is not { CanSave: true } corrida) return;
        Storage.SaveRun(corrida.ToRecord(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        corrida.MarkSaved();
        _guardar.IsEnabled = false;
        _resumen.Text = "Corrida guardada: se compara desde el escritorio.";
    }
}
