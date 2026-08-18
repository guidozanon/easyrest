using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using EasyRest.Models;
using EasyRest.Services;

// Android.Widget viene en los implicit usings del SDK y tiene su propio CheckBox, ProgressBar,
// Button y Orientation: sin los alias, cada uno de esos nombres es ambiguo
using Button = Avalonia.Controls.Button;
using CheckBox = Avalonia.Controls.CheckBox;
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
/// Mientras corre, la configuración se pliega y lo que ocupa la pantalla es el avance: el número
/// grande, la barra y las seis métricas. Es lo único que se mira con la corrida andando, y en 393
/// px no entra junto con los seis campos que ya no vas a tocar.
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
    readonly TextBox _rampa = Ui.Campo("0", "segundos");
    readonly TextBox _demora = Ui.Campo("0", "milisegundos");
    readonly CheckBox _frenar = new()
    {
        Content = new TextBlock
        {
            Text = "Frenar al primer error",
            FontSize = 13,
            Foreground = Ui.Normal,
            VerticalAlignment = VerticalAlignment.Center
        },
        MinHeight = Ui.Toque,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    readonly StackPanel _configuración = new() { Spacing = 10 };
    readonly StackPanel _modo = Ui.Pastillas();
    readonly Border _tarjetaConfiguración;
    readonly StackPanel _avance = new() { Spacing = 10, Margin = new Thickness(0, 0, 0, 4) };
    readonly ContentControl _métricas = new();
    readonly StackPanel _últimas = new();
    readonly TextBlock _resumen = Ui.Nota("");

    readonly Button _correr;
    readonly Button _frenarBoton;
    readonly Button _guardar;

    bool _porDuración;
    RunTab? _corrida;

    public RunnerView(RequestCollection colección, List<RequestItem> requests, string etiqueta,
        Func<EnvironmentModel?> ambiente, Action? alVolver = null)
    {
        _colección = colección;
        _requests = requests;
        _etiqueta = etiqueta;
        _ambiente = ambiente;

        _correr = Ui.PrimarioAsync("Correr", Iconos.Enviar, CorrerAsync);

        _frenarBoton = Ui.Secundario("Frenar", Iconos.Cuadrado, () => _corrida?.Stop());
        _frenarBoton.MinHeight = 36;
        _frenarBoton.Background = Ui.Tinte(Ui.CRojo, 0.14);
        _frenarBoton.Foreground = Ui.Rojo;
        _frenarBoton.IsVisible = false;

        _guardar = Ui.Secundario("Guardar corrida", Iconos.Guardar, Guardar);
        _guardar.MinHeight = 52;
        _guardar.CornerRadius = new CornerRadius(12);
        _guardar.HorizontalAlignment = HorizontalAlignment.Stretch;
        _guardar.IsEnabled = false;

        _tarjetaConfiguración = Ui.Tarjeta(_configuración);
        ArmarConfiguración();

        var cuerpo = new StackPanel
        {
            Margin = new Thickness(16, 14, 16, 20),
            Spacing = 14,
            Children = { _tarjetaConfiguración, _avance, _métricas, _resumen, _últimas }
        };

        var titulo = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Ui.Titulo("Runner"),
                Ui.Nota($"{_etiqueta} · {_colección.Name}")
            }
        };

        var izquierda = alVolver == null
            ? (Control)titulo
            : new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { Ui.BotonIcono(Iconos.Atras, alVolver, Ui.Normal), titulo }
            };

        var pie = new Border
        {
            Background = Ui.Panel,
            BorderBrush = Ui.Superficie,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 12),
            Child = new StackPanel { Spacing = 10, Children = { _correr, _guardar } }
        };

        var raíz = new DockPanel();
        var encabezado = Ui.Encabezado(izquierda, _frenarBoton);
        DockPanel.SetDock(encabezado, Dock.Top);
        DockPanel.SetDock(pie, Dock.Bottom);
        raíz.Children.Add(encabezado);
        raíz.Children.Add(pie);
        raíz.Children.Add(new ScrollViewer { Content = cuerpo });

        MostrarMétricas();
        Content = raíz;
    }

    /// <summary>La configuración se arma una sola vez y lo único que se redibuja son las dos
    /// pastillas del modo: los campos son de la clase, y sacarlos de un panel para meterlos en
    /// otro es justo lo que Avalonia no deja hacer sin desarmar el anterior.</summary>
    void ArmarConfiguración()
    {
        _configuración.Children.Add(Ui.Rotulo("Usuarios virtuales en simultáneo"));
        _configuración.Children.Add(_usuarios);
        _configuración.Children.Add(_modo);
        _configuración.Children.Add(_cantidad);

        _configuración.Children.Add(Ui.Rejilla(2, 10,
            new StackPanel
            {
                Spacing = 8,
                Children = { Ui.Rotulo("Ramp-up en segundos"), _rampa }
            },
            new StackPanel
            {
                Spacing = 8,
                Children = { Ui.Rotulo("Delay en ms"), _demora }
            }));

        _configuración.Children.Add(_frenar);
        PintarModo();
    }

    void PintarModo()
    {
        _modo.Children.Clear();
        _modo.Children.Add(Ui.Pastilla("Iteraciones", !_porDuración,
            () => { _porDuración = false; PintarModo(); }));
        _modo.Children.Add(Ui.Pastilla("Duración (s)", _porDuración,
            () => { _porDuración = true; PintarModo(); }));
        _cantidad.Watermark = _porDuración ? "segundos" : "iteraciones";
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

        // con la corrida andando la configuración ya no se toca: lo que importa es el avance
        _tarjetaConfiguración.IsVisible = false;
        _correr.IsVisible = false;
        _frenarBoton.IsVisible = true;
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
            _tarjetaConfiguración.IsVisible = true;
            _correr.IsVisible = true;
            _frenarBoton.IsVisible = false;
            _guardar.IsEnabled = _corrida?.CanSave == true;
            MostrarMétricas();
        }
    }

    void MostrarMétricas()
    {
        _avance.Children.Clear();
        _últimas.Children.Clear();

        if (_corrida == null)
        {
            _métricas.Content = Ui.Aviso(
                "Correr carga desde un teléfono mide también al teléfono y a su red. Sirve para ver " +
                "si algo responde bien desde afuera, no para sacar números de capacidad.",
                Ui.CAmarillo);
            return;
        }

        _resumen.Text = _corrida.Summary;

        var hechas = (int)_corrida.ProgressValue;
        var total = Math.Max(1, _corrida.ProgressMax);

        var cifra = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = hechas.ToString(),
                    FontSize = 28,
                    FontWeight = FontWeight.Bold,
                    Foreground = Ui.Normal
                },
                new TextBlock
                {
                    Text = _porDuración ? $"de {total:0} s" : $"de {total:0} requests",
                    FontSize = 13,
                    Foreground = Ui.Tenue,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 5)
                }
            }
        };

        var pico = new TextBlock
        {
            Text = _corrida.PeakRpsText,
            FontSize = 13,
            Foreground = Ui.Verde,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 5)
        };

        var fila = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(cifra, 0);
        Grid.SetColumn(pico, 1);
        fila.Children.Add(cifra);
        fila.Children.Add(pico);
        _avance.Children.Add(fila);

        // barra a mano y no un ProgressBar: el control trae el pintado del tema y acá hace falta
        // que sea del alto y del radio del resto de la pantalla
        var proporción = Math.Clamp(_corrida.ProgressValue / total, 0, 1);
        var relleno = new Border
        {
            Background = Ui.Acento,
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0
        };
        var canal = new Border
        {
            Background = Ui.Borde,
            CornerRadius = new CornerRadius(999),
            Height = 8,
            Child = relleno
        };
        canal.SizeChanged += (_, e) => relleno.Width = e.NewSize.Width * proporción;
        relleno.Width = canal.Bounds.Width * proporción;
        _avance.Children.Add(canal);

        _métricas.Content = Ui.Rejilla(3, 10,
            Ui.Metrica("Promedio", _corrida.AvgText),
            Ui.Metrica("p95", _corrida.P95Text),
            Ui.Metrica("p99", _corrida.P99Text),
            Ui.Metrica("Exitosas", _corrida.OkText, Ui.Verde),
            Ui.Metrica("Fallidas", _corrida.FailText, Ui.Rojo),
            Ui.Metrica("Error", _corrida.ErrorRateText));

        // las últimas, que es lo que se mira cuando algo empieza a fallar
        var últimas = _corrida.Results.Skip(Math.Max(0, _corrida.Results.Count - 8)).ToList();
        if (últimas.Count == 0) return;

        var rótulo = Ui.Rotulo("Últimas");
        rótulo.Margin = new Thickness(0, 4, 0, 6);
        _últimas.Children.Add(rótulo);
        foreach (var resultado in últimas) _últimas.Children.Add(FilaDeResultado(resultado));
    }

    static Control FilaDeResultado(RunResult resultado)
    {
        var color = int.TryParse(resultado.Status, out var código)
            ? new SolidColorBrush(Ui.ColorDeEstado(código))
            : Ui.Rojo;

        var estado = new TextBlock
        {
            Text = resultado.Status,
            Width = 44,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = color,
            VerticalAlignment = VerticalAlignment.Center
        };

        var detalle = Ui.Mono($"{resultado.Method} {resultado.Name}", Ui.Subtexto);
        detalle.TextWrapping = TextWrapping.NoWrap;
        detalle.TextTrimming = TextTrimming.CharacterEllipsis;
        detalle.VerticalAlignment = VerticalAlignment.Center;
        detalle.Margin = new Thickness(12, 0);

        var tiempo = new TextBlock
        {
            Text = $"{resultado.TimeMs} ms",
            FontSize = 12,
            Foreground = Ui.Tenue,
            VerticalAlignment = VerticalAlignment.Center
        };

        var fila = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(estado, 0);
        Grid.SetColumn(detalle, 1);
        Grid.SetColumn(tiempo, 2);
        fila.Children.Add(estado);
        fila.Children.Add(detalle);
        fila.Children.Add(tiempo);

        return new Border
        {
            BorderBrush = Ui.Borde,
            BorderThickness = new Thickness(0, 0, 0, 1),
            MinHeight = 52,
            Child = fila
        };
    }

    void Guardar()
    {
        if (_corrida is not { CanSave: true } corrida) return;
        Storage.SaveRun(corrida.ToRecord(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        corrida.MarkSaved();
        _guardar.IsEnabled = false;
        _resumen.Text = "Corrida guardada: se compara desde el escritorio.";
    }
}
