using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest.Models;
using EasyRest.Services;
using EasyRest.Services.Sync;

// Android.Widget viene en los implicit usings y también tiene Button y Orientation
using Button = Avalonia.Controls.Button;
using Orientation = Avalonia.Layout.Orientation;

namespace EasyRest.Android;

/// <summary>La app en el dispositivo: lista de colecciones, editor de request y la conexión con
/// el servidor de sync.
///
/// El layout es adaptativo y se decide por el ancho disponible, no por si el aparato "es" un
/// teléfono o una tablet: un fold desplegado, un teléfono en horizontal y una ventana en modo
/// multiventana son todos el mismo problema, y el ancho es lo único que lo describe bien.
///
/// - Menos de <see cref="AnchoDosPaneles"/>: una columna. La lista ocupa todo y el detalle la
///   reemplaza, con botón de volver.
/// - Más: lista y detalle a la vez, la lista fija a la izquierda. Tocar una request ya no navega
///   a ningún lado, la abre al lado — que es lo que hace que en una tablet se sienta la app de
///   escritorio y no un teléfono estirado.
///
/// El cambio entre los dos modos no reconstruye nada: las dos vistas viven siempre, y lo único
/// que se toca es el ancho de las columnas y la visibilidad. Por eso plegar y desplegar un fold
/// no pierde lo que estabas escribiendo. Que la actividad tampoco se recree es cosa del
/// manifiesto: ver ConfigurationChanges en MainActivity.
///
/// Escrita en C# y no en XAML por el bug de build que documenta docs/ANDROID.md.</summary>
public class ShellView : UserControl
{
    /// <summary>600 unidades independientes de densidad es el corte con el que Android define
    /// "pantalla grande", y coincide con un fold desplegado y con cualquier tablet.</summary>
    const double AnchoDosPaneles = 600;

    /// <summary>Arriba de esto la lista se puede dar el lujo de ser más ancha.</summary>
    const double AnchoHolgado = 900;

    readonly CollectionListView _lista;
    readonly ContentControl _detalle = new();
    readonly ColumnDefinition _columnaLista = new(new GridLength(1, GridUnitType.Star));
    readonly ColumnDefinition _columnaDetalle = new(new GridLength(0, GridUnitType.Pixel));

    readonly TextBlock _titulo = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Ui.Acento };
    readonly TextBlock _estado = new() { FontSize = 11, Foreground = Ui.Tenue, TextWrapping = TextWrapping.Wrap };
    readonly Button _atrás = new() { Content = "‹", FontSize = 20, IsVisible = false, MinHeight = Ui.Toque, Padding = new Thickness(12, 0) };
    readonly ComboBox _selectorAmbiente = new() { MinHeight = Ui.Toque, MinWidth = 140 };

    List<RequestCollection> _colecciones = new();
    List<EnvironmentModel> _ambientes = new();
    EnvironmentModel? _ambiente;

    bool _dosPaneles;
    bool _mostrandoDetalle;
    bool _layoutAplicado;

    public ShellView()
    {
        _lista = new CollectionListView(AbrirRequest);

        _atrás.Click += (_, _) => VolverALista();

        _selectorAmbiente.SelectionChanged += (_, _) =>
        {
            if (_selectorAmbiente.SelectedItem is not EnvironmentModel elegido) return;
            _ambiente = elegido;
            Storage.SetActiveEnvironmentId(elegido.Id);
        };

        var cuerpo = new Grid();
        cuerpo.ColumnDefinitions.Add(_columnaLista);
        cuerpo.ColumnDefinitions.Add(_columnaDetalle);
        Grid.SetColumn(_lista, 0);
        Grid.SetColumn(_detalle, 1);
        cuerpo.Children.Add(_lista);
        cuerpo.Children.Add(_detalle);

        var raíz = new DockPanel { Background = Ui.Fondo };
        var encabezado = Encabezado();
        DockPanel.SetDock(encabezado, Dock.Top);
        raíz.Children.Add(encabezado);
        raíz.Children.Add(cuerpo);
        Content = raíz;

        Recargar();
        VolverALista();
    }

    Control Encabezado()
    {
        var primera = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _atrás, _titulo }
        };

        var acciones = Ui.Barra(
            Ui.AccionAsync("Sincronizar", SincronizarAsync),
            Ui.Accion("Servidor…", MostrarConexión),
            Ui.Accion("Diagnóstico", () => Abrir("Diagnóstico", new SpikeView())));

        return new StackPanel
        {
            Margin = new Thickness(12, 10, 12, 6),
            Spacing = 6,
            Children =
            {
                primera,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { Ui.Rotulo("Ambiente"), _selectorAmbiente }
                },
                acciones,
                _estado
            }
        };
    }

    // ----- Layout adaptativo -----

    /// <summary>Se aplica al arreglar y no en un evento de tamaño: es el único momento en que el
    /// ancho real ya está resuelto. El guardia evita reacomodar en cada pasada de layout, que si
    /// no se realimenta sola.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        AplicarAncho(finalSize.Width);
        return base.ArrangeOverride(finalSize);
    }

    void AplicarAncho(double ancho)
    {
        var dos = ancho >= AnchoDosPaneles;
        if (_layoutAplicado && dos == _dosPaneles) return;

        _dosPaneles = dos;
        _layoutAplicado = true;
        AplicarLayout(ancho);
    }

    void AplicarLayout(double ancho)
    {
        if (_dosPaneles)
        {
            _columnaLista.Width = new GridLength(ancho >= AnchoHolgado ? 360 : 300, GridUnitType.Pixel);
            _columnaDetalle.Width = new GridLength(1, GridUnitType.Star);
            _lista.IsVisible = true;
            _detalle.IsVisible = true;
            _atrás.IsVisible = false;
            return;
        }

        // una sola columna: la que no se muestra queda en cero y oculta, para que no mida ni pinte
        _columnaLista.Width = new GridLength(_mostrandoDetalle ? 0 : 1,
            _mostrandoDetalle ? GridUnitType.Pixel : GridUnitType.Star);
        _columnaDetalle.Width = new GridLength(_mostrandoDetalle ? 1 : 0,
            _mostrandoDetalle ? GridUnitType.Star : GridUnitType.Pixel);
        _lista.IsVisible = !_mostrandoDetalle;
        _detalle.IsVisible = _mostrandoDetalle;
        _atrás.IsVisible = _mostrandoDetalle;
    }

    // ----- Navegación -----

    void AbrirRequest(RequestItem request, RequestCollection colección)
    {
        Abrir(request.Name, new RequestEditorView(request, colección,
            () => _ambiente,
            // guardar puede cambiar el nombre o el método: la lista los muestra, así que se
            // redibuja. No se recarga del disco a propósito: eso crearía objetos nuevos y el
            // editor abierto quedaría editando los viejos.
            () => { _lista.Cargar(_colecciones); MostrarEstadoDeSync(); }));
    }

    void Abrir(string título, Control vista)
    {
        _titulo.Text = título;
        _detalle.Content = vista;
        _mostrandoDetalle = true;
        AplicarLayout(Bounds.Width);
    }

    void VolverALista()
    {
        _titulo.Text = "EasyRest";
        _mostrandoDetalle = false;
        if (!_dosPaneles) _lista.MarcarSeleccion(null);
        MostrarEstadoDeSync();
        AplicarLayout(Bounds.Width);
    }

    // ----- Datos -----

    void Recargar()
    {
        _colecciones = Storage.LoadCollections();
        _ambientes = Storage.LoadEnvironments();

        var activo = Storage.GetActiveEnvironmentId();
        _ambiente = _ambientes.FirstOrDefault(a => a.Id == activo) ?? _ambientes.FirstOrDefault();

        _selectorAmbiente.ItemsSource = _ambientes;
        _selectorAmbiente.SelectedItem = _ambiente;
        _selectorAmbiente.IsVisible = _ambientes.Count > 0;

        _lista.Cargar(_colecciones);
    }

    // ----- Sync -----

    void MostrarEstadoDeSync()
    {
        var binding = SyncBinding.Load(Storage.SyncBindingFile);
        if (!binding.IsSet)
        {
            _estado.Text = "Sin servidor de sync.";
            return;
        }

        _estado.Text = SyncAccountStore.Default.Find(binding.ServerUrl) == null
            ? $"☁ {binding.WorkspaceName} · la sesión venció, reconectate"
            : $"☁ {binding.WorkspaceName} · {binding.ServerUrl}";
    }

    async Task SincronizarAsync()
    {
        var sync = WorkspaceSyncResolver.For(Storage.WorkspaceRoot, Storage.SyncBindingFile,
            Storage.SyncStateFile);

        if (sync == null)
        {
            _estado.Text = WorkspaceSyncResolver.NeedsLogin(Storage.SyncBindingFile)
                ? "La sesión venció. Entrá a «Servidor…» y volvé a conectarte."
                : "Configurá un servidor de sync primero.";
            return;
        }

        _estado.Text = "Sincronizando…";
        try
        {
            // en el teléfono no hay a quién preguntarle cómo resolver conflictos sin interrumpir:
            // el motor guarda la versión del server al lado y gana lo local, que es su default
            var resultado = await sync.SyncAsync();
            _estado.Text = resultado.Message;

            // recargar reemplaza los modelos en memoria, así que el detalle abierto quedaría
            // apuntando a objetos que ya no son los del árbol: se vuelve a la lista
            if (resultado.PulledRemote)
            {
                Recargar();
                VolverALista();
            }
        }
        catch (Exception ex)
        {
            _estado.Text = $"No se pudo sincronizar: {ex.Message}";
        }
    }

    void MostrarConexión() => Abrir("Servidor de sync", new SyncSetupView(this));

    /// <summary>La llama SyncSetupView cuando termina de atar un workspace: hay que volver a la
    /// lista y bajar lo que haya.</summary>
    internal async Task VolverYSincronizarAsync()
    {
        VolverALista();
        await SincronizarAsync();
        Recargar();
    }

    // ----- Piezas compartidas -----
    //
    // Siguen acá porque las usan SpikeView y SyncSetupView; el contenido vive en Ui.

    internal static Button Accion(string texto, Action al) => Ui.Accion(texto, al);
    internal static Button AccionAsync(string texto, Func<Task> al) => Ui.AccionAsync(texto, al);
    internal static TextBlock Parrafo(string texto, IBrush? color = null, double tamaño = 13) =>
        Ui.Parrafo(texto, color, tamaño);
    internal static TextBlock Rotulo(string texto) => Ui.Rotulo(texto);
    internal static Border Tarjeta(params Control[] hijos) => Ui.Tarjeta(hijos);

    internal static IBrush ColorOk => Ui.Verde;
    internal static IBrush ColorError => Ui.Rojo;
    internal static IBrush ColorTenue => Ui.Tenue;
    internal static IBrush ColorNormal => Ui.Normal;
}
