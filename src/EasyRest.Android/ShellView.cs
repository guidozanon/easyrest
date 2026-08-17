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
        _lista = new CollectionListView(AbrirRequest, MenúDeNodo, NuevaColección);

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

        // los diálogos van encima de todo: en móvil no hay ventanas, así que preguntar algo es
        // pintar una capa sobre la app
        var capa = new Grid { IsVisible = false };
        Dialogo.Instalar(capa);

        Content = new Grid { Children = { raíz, capa } };

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
            Ui.Accion("Ambientes", AbrirAmbientes),
            Ui.Accion("Importar", AbrirImportar),
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

    // ----- Crear, renombrar, borrar -----
    //
    // El árbol avisa qué nodo se tocó y acá se decide: la lista no escribe ni en el modelo ni en
    // el disco. Cada cambio se guarda en el acto —crear una carpeta y que no quede es peor que
    // no poder crearla— salvo lo que se edita dentro de una request, que tiene su propio botón.

    void MenúDeNodo(Nodo nodo)
    {
        if (nodo.Request is { } request)
        {
            Dialogo.Opciones(request.Name,
                ("Abrir", () => AbrirRequest(request, nodo.Colección)),
                ("Correr carga…", () => AbrirRunner(nodo.Colección, new List<RequestItem> { request }, request.Name)),
                ("Duplicar", () => Duplicar(nodo, request)),
                ("Renombrar", () => Renombrar(nodo)),
                ("Eliminar", () => Eliminar(nodo)));
            return;
        }

        if (nodo.Carpeta is { } carpeta)
        {
            Dialogo.Opciones(carpeta.Name,
                ("Nueva request", () => NuevaRequest(nodo.Colección, carpeta)),
                ("Nueva subcarpeta", () => NuevaCarpeta(nodo.Colección, carpeta)),
                ("Renombrar", () => Renombrar(nodo)),
                ("Eliminar", () => Eliminar(nodo)));
            return;
        }

        Dialogo.Opciones(nodo.Colección.Name,
            ("Nueva request", () => NuevaRequest(nodo.Colección, null)),
            ("Nueva carpeta", () => NuevaCarpeta(nodo.Colección, null)),
            ("Correr carga…", () => AbrirRunner(nodo.Colección, nodo.Colección.AllRequests.ToList(),
                "(todas las requests)")),
            ("Renombrar", () => Renombrar(nodo)),
            ("Eliminar", () => Eliminar(nodo)));
    }

    void NuevaColección() => Dialogo.Texto("Nueva colección", "", "nombre", nombre =>
    {
        var colección = new RequestCollection { Name = nombre };
        _colecciones.Add(colección);
        GuardarYRefrescar(colección);
    });

    void NuevaRequest(RequestCollection colección, Folder? carpeta) =>
        Dialogo.Texto("Nueva request", "", "nombre", nombre =>
        {
            var request = new RequestItem { Name = nombre };
            (carpeta?.Requests ?? colección.Requests).Add(request);
            GuardarYRefrescar(colección);
            AbrirRequest(request, colección);
        });

    void NuevaCarpeta(RequestCollection colección, Folder? padre) =>
        Dialogo.Texto("Nueva carpeta", "", "nombre", nombre =>
        {
            (padre?.Folders ?? colección.Folders).Add(new Folder { Name = nombre });
            GuardarYRefrescar(colección);
        });

    void Renombrar(Nodo nodo)
    {
        var actual = nodo.Request?.Name ?? nodo.Carpeta?.Name ?? nodo.Colección.Name;
        Dialogo.Texto("Renombrar", actual, "nombre", nombre =>
        {
            if (nodo.Request is { } request) request.Name = nombre;
            else if (nodo.Carpeta is { } carpeta) carpeta.Name = nombre;
            else nodo.Colección.Name = nombre;

            GuardarYRefrescar(nodo.Colección);
            if (nodo.Request != null && _mostrandoDetalle) _titulo.Text = nombre;
        });
    }

    void Duplicar(Nodo nodo, RequestItem request)
    {
        var copia = Clonar(request);
        (nodo.Carpeta?.Requests ?? nodo.Colección.Requests).Add(copia);
        GuardarYRefrescar(nodo.Colección);
    }

    void Eliminar(Nodo nodo)
    {
        var qué = nodo.Request?.Name ?? nodo.Carpeta?.Name ?? nodo.Colección.Name;
        var detalle = nodo.Request != null ? "Se borra la request."
            : nodo.Carpeta != null ? "Se borra la carpeta con todo lo que tenga adentro."
            : "Se borra la colección entera, con sus carpetas y requests.";

        Dialogo.Confirmar($"Eliminar «{qué}»", detalle + " No se puede deshacer.", "Eliminar", () =>
        {
            if (nodo.Request is { } request)
            {
                (nodo.Carpeta?.Requests ?? nodo.Colección.Requests).Remove(request);
                GuardarYRefrescar(nodo.Colección);
            }
            else if (nodo.Carpeta is { } carpeta)
            {
                QuitarCarpeta(nodo.Colección, carpeta);
                GuardarYRefrescar(nodo.Colección);
            }
            else
            {
                Storage.DeleteCollection(nodo.Colección);
                _colecciones.Remove(nodo.Colección);
                _lista.Cargar(_colecciones);
            }

            // el detalle puede estar mostrando justo lo que se borró
            if (_mostrandoDetalle) VolverALista();
        });
    }

    /// <summary>La carpeta puede estar a cualquier profundidad y el nodo no trae a su padre: se
    /// busca. Se materializa la lista antes de tocar nada, porque AllFolders recorre el árbol en
    /// vivo y quitar mientras se enumera revienta.</summary>
    static void QuitarCarpeta(RequestCollection colección, Folder objetivo)
    {
        if (colección.Folders.Remove(objetivo)) return;
        foreach (var carpeta in colección.AllFolders.ToList())
            if (carpeta.Folders.Remove(objetivo)) return;
    }

    /// <summary>Copia a mano y no por serialización: el head corre con el trimming del SDK y el
    /// serializador resuelve por reflexión, que es justo lo que ahí se rompe en silencio.</summary>
    static RequestItem Clonar(RequestItem original)
    {
        var copia = new RequestItem
        {
            Name = original.Name + " (copia)",
            Method = original.Method,
            Url = original.Url,
            Description = original.Description,
            PreRequestScript = original.PreRequestScript,
            TestScript = original.TestScript
        };

        copia.Auth.Type = original.Auth.Type;
        copia.Auth.BearerToken = original.Auth.BearerToken;
        copia.Auth.Username = original.Auth.Username;
        copia.Auth.Password = original.Auth.Password;
        copia.Auth.ApiKeyName = original.Auth.ApiKeyName;
        copia.Auth.ApiKeyValue = original.Auth.ApiKeyValue;
        copia.Auth.ApiKeyIn = original.Auth.ApiKeyIn;

        copia.Body.Type = original.Body.Type;
        copia.Body.Raw = original.Body.Raw;

        foreach (var item in original.Body.FormItems) copia.Body.FormItems.Add(Clonar(item));
        foreach (var item in original.Headers) copia.Headers.Add(Clonar(item));
        foreach (var item in original.QueryParams) copia.QueryParams.Add(Clonar(item));
        return copia;
    }

    static KeyValueItem Clonar(KeyValueItem item) =>
        new() { Enabled = item.Enabled, Key = item.Key, Value = item.Value };

    void GuardarYRefrescar(RequestCollection colección)
    {
        try
        {
            Storage.SaveCollection(colección);
            _lista.Cargar(_colecciones);
        }
        catch (Exception ex)
        {
            _estado.Text = $"No se pudo guardar: {ex.Message}";
        }
    }

    // ----- Otras pantallas -----

    void AbrirAmbientes() => Abrir("Ambientes", new EnvironmentsView(_ambientes, _ambiente, () =>
    {
        // el selector de la barra y el ambiente con el que se manda tienen que seguir al editor
        var activo = Storage.GetActiveEnvironmentId();
        _ambiente = _ambientes.FirstOrDefault(a => a.Id == activo) ?? _ambientes.FirstOrDefault();
        _selectorAmbiente.ItemsSource = null;
        _selectorAmbiente.ItemsSource = _ambientes;
        _selectorAmbiente.SelectedItem = _ambiente;
        _selectorAmbiente.IsVisible = _ambientes.Count > 0;
    }));

    void AbrirImportar() => Abrir("Importar", new ImportView(() => _colecciones, colección =>
    {
        // puede ser una colección nueva o una que ya estaba (un cURL agregado adentro)
        if (!_colecciones.Contains(colección)) _colecciones.Add(colección);
        _lista.Cargar(_colecciones);
    }));

    void AbrirRunner(RequestCollection colección, List<RequestItem> requests, string etiqueta) =>
        Abrir("Runner", new RunnerView(colección, requests, etiqueta, () => _ambiente));

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
