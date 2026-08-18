using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest.Models;
using EasyRest.Services;
using EasyRest.Services.Sync;

// Android.Widget viene en los implicit usings y también tiene Button y Orientation
using Button = Avalonia.Controls.Button;
using Forma = Avalonia.Controls.Shapes.Path;
using Orientation = Avalonia.Layout.Orientation;

namespace EasyRest.Android;

/// <summary>La app en el dispositivo: colecciones, ambientes, runner y el resto.
///
/// **La navegación es una barra abajo**, con cuatro destinos. Antes todo colgaba del encabezado
/// —cinco botones apretados arriba de la lista— y el resultado era que ni se veían ni se llegaba
/// bien: en un teléfono el pulgar llega abajo, no arriba. Además cada destino se guarda armado, así
/// que ir a Ambientes y volver no rearma la lista ni pierde la request abierta.
///
/// Dentro de Colecciones, el layout es adaptativo y se decide por el ancho disponible, no por si
/// el aparato "es" un teléfono o una tablet: un fold desplegado, un teléfono en horizontal y una
/// ventana en modo multiventana son todos el mismo problema, y el ancho es lo único que lo
/// describe bien.
///
/// - Menos de <see cref="AnchoDosPaneles"/>: una columna. La lista ocupa todo y el detalle la
///   reemplaza, con botón de volver.
/// - Más: lista y detalle a la vez, la lista fija a la izquierda. Tocar una request ya no navega
///   a ningún lado, la abre al lado — que es lo que hace que en una tablet se sienta la app de
///   escritorio y no un teléfono estirado. La lista además se puede plegar con ☰ para darle todo
///   el ancho a la request.
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

    // ----- Colecciones -----

    readonly CollectionListView _lista;
    readonly ContentControl _detalle = new();
    readonly ColumnDefinition _columnaLista = new(new GridLength(1, GridUnitType.Star));
    readonly ColumnDefinition _columnaDetalle = new(new GridLength(0, GridUnitType.Pixel));

    readonly TextBlock _titulo = new()
    {
        Text = "Colecciones",
        FontSize = 17,
        FontWeight = FontWeight.SemiBold,
        Foreground = Ui.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    readonly Forma _rayo = Ui.Icono(Iconos.Rayo, 20, Ui.Acento, relleno: true);
    readonly Button _atrás;
    readonly Button _colapsar;
    readonly Button _chipAmbiente = new();

    // ----- Pestañas -----

    readonly Control _pestañaColecciones;
    readonly ContentControl _pestañaAmbientes = new();
    readonly ContentControl _pestañaRunner = new();
    readonly ContentControl _pestañaMas = new();
    readonly List<(Button Boton, Forma Icono, TextBlock Etiqueta)> _navegación = new();
    readonly List<Control> _pestañas = new();

    MasView? _mas;

    List<RequestCollection> _colecciones = new();
    List<EnvironmentModel> _ambientes = new();
    EnvironmentModel? _ambiente;

    bool _dosPaneles;
    bool _mostrandoDetalle;
    bool _layoutAplicado;

    /// <summary>Con dos paneles, la lista se puede plegar para que el detalle ocupe todo. Es
    /// estado de la sesión y no se guarda: al abrir, la lista se ve.</summary>
    bool _listaPlegada;

    public ShellView()
    {
        _lista = new CollectionListView(AbrirRequest, MenúDeNodo, NuevaColección);

        _atrás = Ui.BotonIcono(Iconos.Atras, VolverALista, Ui.Normal);
        _atrás.IsVisible = false;

        _colapsar = Ui.BotonIcono(Iconos.Lineas, () =>
        {
            _listaPlegada = !_listaPlegada;
            AplicarLayout(Bounds.Width);
        }, Ui.Normal);
        _colapsar.IsVisible = false;

        _chipAmbiente.Click += (_, _) => ElegirAmbiente();

        var cuerpo = new Grid();
        cuerpo.ColumnDefinitions.Add(_columnaLista);
        cuerpo.ColumnDefinitions.Add(_columnaDetalle);
        Grid.SetColumn(_lista, 0);
        Grid.SetColumn(_detalle, 1);
        cuerpo.Children.Add(_lista);
        cuerpo.Children.Add(_detalle);

        var colecciones = new DockPanel();
        var encabezado = Encabezado();
        DockPanel.SetDock(encabezado, Dock.Top);
        colecciones.Children.Add(encabezado);
        colecciones.Children.Add(cuerpo);
        _pestañaColecciones = colecciones;

        var pestañas = new Grid();
        foreach (var pestaña in new Control[]
                 { _pestañaColecciones, _pestañaAmbientes, _pestañaRunner, _pestañaMas })
        {
            _pestañas.Add(pestaña);
            pestañas.Children.Add(pestaña);
        }

        var raíz = new DockPanel { Background = Ui.Fondo };
        var navegación = Navegacion();
        DockPanel.SetDock(navegación, Dock.Bottom);
        raíz.Children.Add(navegación);
        raíz.Children.Add(pestañas);

        // los diálogos van encima de todo: en móvil no hay ventanas, así que preguntar algo es
        // pintar una capa sobre la app
        var capa = new Grid { IsVisible = false };
        Dialogo.Instalar(capa);

        Content = new Grid { Children = { raíz, capa } };

        Recargar();
        VolverALista();
        Ir(0);
    }

    // ----- Encabezado de Colecciones -----

    Control Encabezado()
    {
        _rayo.Margin = new Thickness(4, 0, 8, 0);
        var izquierda = Ui.Linea(_titulo, _atrás, _colapsar, _rayo);
        izquierda.VerticalAlignment = VerticalAlignment.Center;

        PintarChip();
        return Ui.Encabezado(izquierda, _chipAmbiente);
    }

    /// <summary>El ambiente activo, siempre a la vista y a un toque. Es la pastilla del diseño:
    /// punto verde, nombre y chevron. Mandar contra el ambiente equivocado es el error caro, así
    /// que contra cuál estás mandando no puede estar escondido en un menú.</summary>
    void PintarChip()
    {
        var hay = _ambiente != null;
        var contenido = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (hay)
            contenido.Children.Add(new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(999),
                Background = Ui.Verde,
                VerticalAlignment = VerticalAlignment.Center
            });

        contenido.Children.Add(new TextBlock
        {
            Text = _ambiente?.Name ?? "Sin ambiente",
            FontSize = 12,
            Foreground = hay ? Ui.Normal : Ui.Tenue,
            MaxWidth = 130,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        contenido.Children.Add(Ui.Icono(Iconos.ChevronAbajo, 12, Ui.Tenue));

        _chipAmbiente.Content = contenido;
        _chipAmbiente.Background = Ui.Superficie;
        _chipAmbiente.CornerRadius = new CornerRadius(999);
        _chipAmbiente.Padding = new Thickness(12, 0);
        _chipAmbiente.MinHeight = 36;
    }

    void ElegirAmbiente()
    {
        if (_ambientes.Count == 0)
        {
            Ir(1);
            return;
        }

        var opciones = _ambientes
            .Select(a => ((a.Id == _ambiente?.Id ? "● " : "○ ") + a.Name, (Action)(() => Activar(a))))
            .Append(("Administrar ambientes…", () => Ir(1)))
            .ToArray();
        Dialogo.Opciones("Ambiente activo", opciones);
    }

    void Activar(EnvironmentModel ambiente)
    {
        _ambiente = ambiente;
        Storage.SetActiveEnvironmentId(ambiente.Id);
        PintarChip();
    }

    // ----- Barra de navegación -----

    Control Navegacion()
    {
        var barra = new Grid();
        // el relleno va por destino: los tres puntos son discos y se pintan con Fill, el resto
        // son trazos y se pintan con Stroke
        var destinos = new (string Etiqueta, Geometry Icono, bool Relleno)[]
        {
            ("Colecciones", Iconos.Lista, false),
            ("Ambientes", Iconos.Globo, false),
            ("Runner", Iconos.Enviar, false),
            ("Más", Iconos.Puntos, true)
        };

        for (var i = 0; i < destinos.Length; i++)
        {
            barra.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var (etiqueta, geometría, relleno) = destinos[i];
            var icono = Ui.Icono(geometría, 22, Ui.Tenue, relleno);

            // cada dibujo tiene su propio alto —el ☰ es chato, el globo cuadrado, los puntos una
            // línea—, así que el ícono va dentro de una caja fija: sin eso, cada destino empujaba
            // su rótulo a una altura distinta y la barra quedaba escalonada
            var casilla = new Border { Height = 24, Child = icono };

            var texto = new TextBlock
            {
                Text = etiqueta,
                FontSize = 11,
                Foreground = Ui.Tenue,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var destino = i;
            var boton = new Button
            {
                Content = new StackPanel { Spacing = 4, Children = { casilla, texto } },
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(0, 6),
                MinHeight = 56,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            boton.Click += (_, _) => Ir(destino);

            Grid.SetColumn(boton, i);
            barra.Children.Add(boton);
            _navegación.Add((boton, icono, texto));
        }

        return new Border
        {
            Background = Ui.Corteza,
            BorderBrush = Ui.Superficie,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 4, 0, 8),
            Child = barra
        };
    }

    void Ir(int destino)
    {
        for (var i = 0; i < _pestañas.Count; i++) _pestañas[i].IsVisible = i == destino;
        for (var i = 0; i < _navegación.Count; i++)
        {
            var activo = i == destino;
            var pincel = activo ? Ui.Acento : Ui.Tenue;
            var forma = _navegación[i].Icono;
            if (forma.Fill != null) forma.Fill = pincel; else forma.Stroke = pincel;
            _navegación[i].Etiqueta.Foreground = pincel;
        }

        // cada destino se arma al entrar la primera vez, y los que dependen de datos que pudieron
        // cambiar en otra pestaña —ambientes, sync— se refrescan cada vez
        switch (destino)
        {
            case 1:
                _ambientes = Storage.LoadEnvironments();
                ResolverAmbiente();
                _pestañaAmbientes.Content = new EnvironmentsView(_ambientes, _ambiente, () =>
                {
                    ResolverAmbiente();
                    PintarChip();
                });
                break;

            case 2:
                if (_pestañaRunner.Content == null) MostrarElectorDeRunner();
                break;

            // volver a la pestaña no descarta la pantalla que tenga apilada adentro; sólo la
            // portada se refresca, porque el estado del sync pudo cambiar en otro lado
            case 3:
                if (_pestañaMas.Content == null) VolverAMas();
                else if (ReferenceEquals(_pestañaMas.Content, _mas)) _mas!.Refrescar();
                break;
        }
    }

    void ResolverAmbiente()
    {
        var activo = Storage.GetActiveEnvironmentId();
        _ambiente = _ambientes.FirstOrDefault(a => a.Id == activo) ?? _ambientes.FirstOrDefault();
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
        _lista.ModoPanel(_dosPaneles);

        if (_dosPaneles)
        {
            // plegada, la lista deja todo el ancho para la request abierta. El botón queda igual
            // en su lugar y se pinta con el acento cuando está plegada: es lo único que dice que
            // la lista sigue ahí, y sin eso el panel parece haber desaparecido.
            _columnaLista.Width = _listaPlegada
                ? new GridLength(0, GridUnitType.Pixel)
                : new GridLength(ancho >= AnchoHolgado ? 360 : 300, GridUnitType.Pixel);
            _columnaDetalle.Width = new GridLength(1, GridUnitType.Star);
            _lista.IsVisible = !_listaPlegada;
            _detalle.IsVisible = true;
            _rayo.IsVisible = true;
            _atrás.IsVisible = false;
            _colapsar.IsVisible = true;
            _colapsar.Background = _listaPlegada ? Ui.Tinte(Ui.CAcento) : Brushes.Transparent;
            return;
        }

        // en una columna no hay nada que plegar: la lista y el detalle ya se turnan
        _colapsar.IsVisible = false;

        // una sola columna: la que no se muestra queda en cero y oculta, para que no mida ni pinte
        _columnaLista.Width = new GridLength(_mostrandoDetalle ? 0 : 1,
            _mostrandoDetalle ? GridUnitType.Pixel : GridUnitType.Star);
        _columnaDetalle.Width = new GridLength(_mostrandoDetalle ? 1 : 0,
            _mostrandoDetalle ? GridUnitType.Star : GridUnitType.Pixel);
        _lista.IsVisible = !_mostrandoDetalle;
        _detalle.IsVisible = _mostrandoDetalle;
        _atrás.IsVisible = _mostrandoDetalle;
        _rayo.IsVisible = !_mostrandoDetalle;
    }

    // ----- Navegación dentro de Colecciones -----

    void AbrirRequest(RequestItem request, RequestCollection colección)
    {
        Abrir(request.Name, new RequestEditorView(request, colección,
            () => _ambiente,
            // guardar puede cambiar el nombre o el método: la lista los muestra, así que se
            // redibuja. No se recarga del disco a propósito: eso crearía objetos nuevos y el
            // editor abierto quedaría editando los viejos.
            () => _lista.Cargar(_colecciones)));
    }

    void Abrir(string título, Control vista)
    {
        _titulo.Text = título;
        _detalle.Content = vista;
        _mostrandoDetalle = true;
        Ir(0);
        AplicarLayout(Bounds.Width);
    }

    void VolverALista()
    {
        _titulo.Text = "Colecciones";
        _mostrandoDetalle = false;
        if (!_dosPaneles) _lista.MarcarSeleccion(null);
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
            Dialogo.Confirmar("No se pudo guardar", ex.Message, "Listo", () => { });
        }
    }

    // ----- Runner -----

    void AbrirRunner(RequestCollection colección, List<RequestItem> requests, string etiqueta)
    {
        _pestañaRunner.Content = new RunnerView(colección, requests, etiqueta, () => _ambiente,
            MostrarElectorDeRunner);
        Ir(2);
    }

    /// <summary>La pestaña abierta sin haber elegido nada: en vez de una pantalla vacía, la lista
    /// de colecciones para correr entera. Correr una colección completa es el caso normal; una
    /// request suelta se elige desde su menú en el árbol.</summary>
    void MostrarElectorDeRunner()
    {
        var pila = new StackPanel { Margin = new Thickness(16, 16, 16, 24), Spacing = 12 };
        pila.Children.Add(Ui.Parrafo(
            "Elegí qué correr. Para una request sola, abrí su menú en la lista y tocá «Correr carga…».",
            Ui.Subtexto));

        if (_colecciones.Count == 0)
        {
            pila.Children.Add(Ui.Nota("Todavía no hay colecciones."));
        }
        else
        {
            foreach (var colección in _colecciones)
            {
                var elegida = colección;
                var cuántas = colección.AllRequests.Count();
                var boton = Ui.Secundario($"{colección.Name} · {cuántas} requests", Iconos.Enviar,
                    () => AbrirRunner(elegida, elegida.AllRequests.ToList(), "(todas las requests)"));
                boton.HorizontalAlignment = HorizontalAlignment.Stretch;
                boton.HorizontalContentAlignment = HorizontalAlignment.Left;
                pila.Children.Add(boton);
            }
        }

        pila.Children.Add(Ui.Aviso(
            "Correr carga desde un teléfono mide también al teléfono y a su red. Sirve para ver si " +
            "algo responde bien desde afuera, no para sacar números de capacidad.", Ui.CAmarillo));

        var raíz = new DockPanel();
        var encabezado = Ui.Encabezado(Ui.Titulo("Runner"));
        DockPanel.SetDock(encabezado, Dock.Top);
        raíz.Children.Add(encabezado);
        raíz.Children.Add(new ScrollViewer { Content = pila });
        _pestañaRunner.Content = raíz;
    }

    // ----- Más -----

    void VolverAMas()
    {
        _mas ??= new MasView(SincronizarAsync, MostrarConexión, AbrirImportar,
            () => MostrarEnMas("Diagnóstico", new SpikeView()));
        _mas.Refrescar();
        _pestañaMas.Content = _mas;
    }

    /// <summary>Las pantallas de la cuarta pestaña se apilan adentro de ella: el destino sigue
    /// marcado abajo y volver es un solo toque, sin sacarte de donde estabas. La vista trae su
    /// propio scroll —anidar dos es lo que hace que una lista larga se trabe—; acá sólo se le
    /// pone el encabezado.</summary>
    void MostrarEnMas(string título, Control vista)
    {
        var atrás = Ui.BotonIcono(Iconos.Atras, VolverAMas, Ui.Normal);
        var izquierda = Ui.Linea(Ui.Titulo(título), atrás);
        izquierda.VerticalAlignment = VerticalAlignment.Center;

        var raíz = new DockPanel();
        var encabezado = Ui.Encabezado(izquierda);
        DockPanel.SetDock(encabezado, Dock.Top);
        raíz.Children.Add(encabezado);
        raíz.Children.Add(vista);

        _pestañaMas.Content = raíz;
        Ir(3);
    }

    void AbrirImportar()
    {
        // ImportView trae su propio encabezado con el botón de volver
        _pestañaMas.Content = new ImportView(() => _colecciones, colección =>
        {
            // puede ser una colección nueva o una que ya estaba (un cURL agregado adentro)
            if (!_colecciones.Contains(colección)) _colecciones.Add(colección);
            _lista.Cargar(_colecciones);
        }, () => { VolverAMas(); Ir(0); });
        Ir(3);
    }

    void MostrarConexión() => MostrarEnMas("Servidor de sync", new SyncSetupView(this));

    // ----- Datos -----

    void Recargar()
    {
        _colecciones = Storage.LoadCollections();
        _ambientes = Storage.LoadEnvironments();
        ResolverAmbiente();
        PintarChip();
        _lista.Cargar(_colecciones);
    }

    // ----- Sync -----

    async Task SincronizarAsync()
    {
        var sync = WorkspaceSyncResolver.For(Storage.WorkspaceRoot, Storage.SyncBindingFile,
            Storage.SyncStateFile);

        if (sync == null)
        {
            _mas?.Contar(WorkspaceSyncResolver.NeedsLogin(Storage.SyncBindingFile)
                ? "La sesión venció. Entrá a «Servidor de sync» y volvé a conectarte."
                : "Configurá un servidor de sync primero.");
            return;
        }

        try
        {
            // en el teléfono no hay a quién preguntarle cómo resolver conflictos sin interrumpir:
            // el motor guarda la versión del server al lado y gana lo local, que es su default
            var resultado = await sync.SyncAsync();
            _mas?.Contar(resultado.Message);

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
            _mas?.Contar($"No se pudo sincronizar: {ex.Message}");
        }
    }

    /// <summary>La llama SyncSetupView cuando termina de atar un workspace: hay que bajar lo que
    /// haya y mostrarlo.</summary>
    internal async Task VolverYSincronizarAsync()
    {
        VolverAMas();
        await SincronizarAsync();
        Recargar();
        VolverALista();
        Ir(0);
    }
}
