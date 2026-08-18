using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest.Models;

using Button = Avalonia.Controls.Button;

namespace EasyRest.Android;

/// <summary>Un nodo del árbol, para que la lista pueda avisar sobre qué se pidió el menú sin
/// saber qué se va a hacer con él. Carpeta y Request en null = la colección entera; Carpeta con
/// Request en null = la carpeta. Carpeta es además el padre de la request, que es lo que hace
/// falta para borrarla o moverla.</summary>
internal record Nodo(RequestCollection Colección, Folder? Carpeta, RequestItem? Request);

/// <summary>El árbol de colecciones: el panel izquierdo en tablet y fold desplegado, la pantalla
/// de entrada en teléfono.
///
/// Cada request es una fila de 60 px con su método en color —GET azul, POST verde, PUT durazno,
/// DELETE rojo— y la URL abajo en monoespaciada. El color del método es lo que hace que una lista
/// de doscientas se lea de un vistazo; antes eran todas tarjetas grises iguales.
///
/// El buscador es lo que hace usable un workspace real desde un teléfono: con doscientas requests
/// importadas de un OpenAPI, bajar scrolleando no es una opción. Mientras hay filtro se muestra
/// todo lo que coincide sin respetar el plegado, porque esconder un resultado detrás de una
/// carpeta cerrada haría que la búsqueda parezca rota.
///
/// La vista no toca el modelo ni el disco: avisa qué nodo se eligió y el shell decide.</summary>
internal class CollectionListView : UserControl
{
    readonly Action<RequestItem, RequestCollection> _alElegir;
    readonly Action<Nodo> _alMenu;
    readonly StackPanel _lista = new();
    readonly TextBox _busqueda;
    readonly Border _fondo;
    readonly Border _banda;
    readonly Button _nueva;
    readonly Dictionary<RequestItem, Border> _filas = new();

    List<RequestCollection> _colecciones = new();
    RequestItem? _seleccionada;

    public CollectionListView(Action<RequestItem, RequestCollection> alElegir, Action<Nodo> alMenu,
        Action alNuevaColección)
    {
        _alElegir = alElegir;
        _alMenu = alMenu;

        _busqueda = Ui.Campo("", "Buscar request…");
        _busqueda.TextChanged += (_, _) => Redibujar();

        var lupa = Ui.Icono(Iconos.Buscar, 16, Ui.Tenue);
        lupa.Margin = new Thickness(12, 0, 0, 0);
        var buscador = new Grid();
        buscador.Children.Add(_busqueda);
        buscador.Children.Add(lupa);
        _busqueda.Padding = new Thickness(38, 4, 12, 4);
        lupa.HorizontalAlignment = HorizontalAlignment.Left;

        var barra = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _nueva = Ui.BotonIcono(Iconos.Mas, alNuevaColección, Ui.Normal, Ui.Superficie);
        _nueva.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(buscador, 0);
        Grid.SetColumn(_nueva, 1);
        barra.Children.Add(buscador);
        barra.Children.Add(_nueva);

        // el buscador va sobre el color del panel y con el mismo borde de abajo que el encabezado
        // del shell: así los dos se leen como un solo bloque fijo arriba de la lista
        _banda = new Border
        {
            Background = Ui.Panel,
            BorderBrush = Ui.Superficie,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = barra
        };

        var raíz = new DockPanel();
        DockPanel.SetDock(_banda, Dock.Top);
        raíz.Children.Add(_banda);
        raíz.Children.Add(new ScrollViewer { Content = _lista });

        _fondo = new Border { Child = raíz };
        ModoPanel(false);
        Content = _fondo;
    }

    /// <summary>Con dos paneles la lista es una columna propia y se pinta entera del color del
    /// panel, con su línea a la derecha: así se lee como el panel lateral del escritorio. En una
    /// columna la lista es la pantalla, va sobre el fondo, y lo único pintado es la banda del
    /// buscador — que ahí sí tiene que separarse de las filas.</summary>
    public void ModoPanel(bool dosPaneles)
    {
        _fondo.Background = dosPaneles ? Ui.Panel : Ui.Fondo;
        _fondo.BorderBrush = Ui.Superficie;
        _fondo.BorderThickness = new Thickness(0, 0, dosPaneles ? 1 : 0, 0);

        _banda.Background = dosPaneles ? Brushes.Transparent : Ui.Panel;
        _banda.BorderThickness = new Thickness(0, 0, 0, dosPaneles ? 0 : 1);
        // el buscador respira arriba: pegado al borde del encabezado se leía como si se hubiera
        // desbordado desde ahí
        _banda.Padding = new Thickness(16, 12, 8, 12);

        // de panel el buscador es un accesorio y no la pantalla: más chico, como en el diseño del
        // fold. En una columna es lo primero que se toca y va del tamaño del dedo.
        var alto = dosPaneles ? 40 : Ui.Toque - 4;
        _busqueda.MinHeight = alto;
        _busqueda.Height = alto;
        _nueva.Width = alto;
        _nueva.MinHeight = alto;
        _nueva.Height = alto;
    }

    public void Cargar(List<RequestCollection> colecciones)
    {
        _colecciones = colecciones;
        Redibujar();
    }

    /// <summary>Pinta cuál está abierta. Sólo se nota con dos paneles —en teléfono el detalle tapa
    /// la lista—, pero es justo ahí donde hace falta para no perderse.</summary>
    public void MarcarSeleccion(RequestItem? request)
    {
        _seleccionada = request;
        foreach (var (item, fila) in _filas)
            fila.Background = ReferenceEquals(item, request) ? Ui.Superficie : Brushes.Transparent;
    }

    public void Redibujar()
    {
        _lista.Children.Clear();
        _filas.Clear();

        var filtro = (_busqueda.Text ?? "").Trim();

        if (_colecciones.Count == 0)
        {
            _lista.Children.Add(Vacío());
            return;
        }

        var hubo = false;
        foreach (var colección in _colecciones)
        {
            var requests = colección.AllRequests.Count(r => Coincide(r, filtro));
            if (requests == 0 && filtro.Length > 0) continue;
            hubo = true;

            var elegida = colección;
            _lista.Children.Add(Encabezado(colección.Name, requests, colección.IsExpandedInTree, 0,
                () => { elegida.IsExpandedInTree = !elegida.IsExpandedInTree; Redibujar(); },
                () => _alMenu(new Nodo(elegida, null, null)), Ui.Acento, null));

            if (!Abierto(colección.IsExpandedInTree, filtro)) continue;
            AgregarRequests(colección.Requests, colección, null, filtro, 1);
            foreach (var carpeta in colección.Folders) AgregarCarpeta(carpeta, colección, filtro, 1);
        }

        if (!hubo) _lista.Children.Add(Ui.Parrafo($"Nada coincide con «{filtro}».", Ui.Tenue));

        MarcarSeleccion(_seleccionada);
    }

    Control Vacío()
    {
        var pila = new StackPanel { Spacing = 14, Margin = new Thickness(24, 48, 24, 24) };
        var icono = Ui.Icono(Iconos.Nube, 40, Ui.Superficie);
        icono.HorizontalAlignment = HorizontalAlignment.Center;
        pila.Children.Add(icono);

        var titulo = Ui.Parrafo("Todavía no hay colecciones", Ui.Subtexto, 15);
        titulo.TextAlignment = TextAlignment.Center;
        pila.Children.Add(titulo);

        var texto = Ui.Parrafo(
            "Creála con +, importá un OpenAPI pegando su link, o conectate a un servidor de sync: " +
            "las colecciones bajan solas.", Ui.Tenue, 13);
        texto.TextAlignment = TextAlignment.Center;
        pila.Children.Add(texto);
        return pila;
    }

    /// <summary>Con filtro activo el plegado no manda: si algo coincide, se ve.</summary>
    static bool Abierto(bool expandido, string filtro) => expandido || filtro.Length > 0;

    static bool Coincide(RequestItem request, string filtro) =>
        filtro.Length == 0 ||
        request.Name.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
        request.Url.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
        request.Method.Contains(filtro, StringComparison.OrdinalIgnoreCase);

    void AgregarCarpeta(Folder carpeta, RequestCollection dueña, string filtro, int nivel)
    {
        var coincidencias = carpeta.AllRequests.Count(r => Coincide(r, filtro));
        if (coincidencias == 0 && filtro.Length > 0) return;

        var elegida = carpeta;
        _lista.Children.Add(Encabezado(carpeta.Name, coincidencias, carpeta.IsExpandedInTree, nivel,
            () => { elegida.IsExpandedInTree = !elegida.IsExpandedInTree; Redibujar(); },
            () => _alMenu(new Nodo(dueña, elegida, null)), Ui.Subtexto, Iconos.Carpeta));

        if (!Abierto(carpeta.IsExpandedInTree, filtro)) return;
        AgregarRequests(carpeta.Requests, dueña, carpeta, filtro, nivel + 1);
        foreach (var hija in carpeta.Folders) AgregarCarpeta(hija, dueña, filtro, nivel + 1);
    }

    void AgregarRequests(IEnumerable<RequestItem> requests, RequestCollection dueña, Folder? padre,
        string filtro, int nivel)
    {
        // se copia la secuencia: el menú puede borrar mientras esto recorre
        foreach (var request in requests.ToList())
        {
            if (!Coincide(request, filtro)) continue;

            var textos = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            textos.Children.Add(new TextBlock
            {
                Text = request.Name,
                FontSize = 14,
                Foreground = Ui.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            textos.Children.Add(new TextBlock
            {
                Text = request.Url,
                FontSize = 11,
                FontFamily = "Consolas,Menlo,monospace",
                Foreground = Ui.Tenue,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var contenido = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var etiqueta = Ui.EtiquetaMetodo(request.Method);
            etiqueta.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(etiqueta, 0);
            Grid.SetColumn(textos, 1);
            contenido.Children.Add(etiqueta);
            contenido.Children.Add(textos);

            var elegida = request;
            var boton = new Button
            {
                Content = contenido,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                MinHeight = 60,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            boton.Click += (_, _) =>
            {
                MarcarSeleccion(elegida);
                _alElegir(elegida, dueña);
            };

            var menú = Ui.BotonIcono(Iconos.Puntos, () => _alMenu(new Nodo(dueña, padre, elegida)),
                relleno: true);

            var fila = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(boton, 0);
            Grid.SetColumn(menú, 1);
            fila.Children.Add(boton);
            fila.Children.Add(menú);

            var marco = new Border
            {
                BorderBrush = Ui.Borde,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16 + nivel * 12, 0, 8, 0),
                Child = fila
            };
            _filas[request] = marco;
            _lista.Children.Add(marco);
        }
    }

    Control Encabezado(string texto, int cuantas, bool expandido, int nivel, Action alTocar,
        Action alMenu, IBrush color, Geometry? icono)
    {
        var contenido = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        var chevron = Ui.Icono(expandido ? Iconos.ChevronAbajo : Iconos.Chevron, 13, color);
        contenido.Children.Add(chevron);
        if (icono != null) contenido.Children.Add(Ui.Icono(icono, 15, color));
        contenido.Children.Add(new TextBlock
        {
            Text = texto,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = color,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        contenido.Children.Add(new TextBlock
        {
            Text = cuantas.ToString(),
            FontSize = 11,
            Foreground = Ui.Tenue,
            VerticalAlignment = VerticalAlignment.Center
        });

        var boton = new Button
        {
            Content = contenido,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        boton.Click += (_, _) => alTocar();

        var menú = Ui.BotonIcono(Iconos.Puntos, alMenu, relleno: true);

        var fila = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(boton, 0);
        Grid.SetColumn(menú, 1);
        fila.Children.Add(boton);
        fila.Children.Add(menú);

        return new Border
        {
            Padding = new Thickness(16 + nivel * 12, 6, 8, 2),
            Child = fila
        };
    }
}
