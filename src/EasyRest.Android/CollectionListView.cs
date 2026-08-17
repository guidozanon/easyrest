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
/// Carpetas plegables, un buscador arriba y un menú «⋯» por nodo. El buscador es lo que hace
/// usable un workspace real desde un teléfono: con doscientas requests importadas de un OpenAPI,
/// bajar scrolleando no es una opción, y escribir tres letras sí. Mientras hay filtro se muestra
/// todo lo que coincide sin respetar el plegado, porque esconder un resultado detrás de una
/// carpeta cerrada haría que la búsqueda parezca rota.
///
/// La vista no toca el modelo ni el disco: avisa qué nodo se eligió y el shell decide.</summary>
internal class CollectionListView : UserControl
{
    readonly Action<RequestItem, RequestCollection> _alElegir;
    readonly Action<Nodo> _alMenu;
    readonly StackPanel _lista = new() { Spacing = 4, Margin = new Thickness(10, 0, 10, 14) };
    readonly TextBox _busqueda;
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

        var nueva = Ui.Accion("+", alNuevaColección);
        nueva.MinWidth = Ui.Toque;

        var barra = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(10, 0, 10, 8) };
        Grid.SetColumn(_busqueda, 0);
        Grid.SetColumn(nueva, 1);
        nueva.Margin = new Thickness(8, 0, 0, 0);
        barra.Children.Add(_busqueda);
        barra.Children.Add(nueva);

        var raíz = new DockPanel();
        DockPanel.SetDock(barra, Dock.Top);
        raíz.Children.Add(barra);
        raíz.Children.Add(new ScrollViewer { Content = _lista });
        Content = raíz;
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
            fila.Background = ReferenceEquals(item, request) ? Ui.PanelAlto : Ui.Panel;
    }

    public void Redibujar()
    {
        _lista.Children.Clear();
        _filas.Clear();

        var filtro = (_busqueda.Text ?? "").Trim();

        if (_colecciones.Count == 0)
        {
            _lista.Children.Add(Ui.Parrafo(
                "Todavía no hay colecciones en este teléfono.\n\n" +
                "Creá una con «+», importá un OpenAPI o un cURL, o conectate a un servidor de " +
                "sync y elegí un workspace: las colecciones bajan solas.",
                Ui.Tenue));
            return;
        }

        var hubo = false;
        foreach (var colección in _colecciones)
        {
            var requests = colección.AllRequests.Count(r => Coincide(r, filtro));
            if (requests == 0 && filtro.Length > 0) continue;
            hubo = true;

            var elegida = colección;
            _lista.Children.Add(Encabezado(colección.Name, requests, colección.IsExpandedInTree,
                () => { elegida.IsExpandedInTree = !elegida.IsExpandedInTree; Redibujar(); },
                () => _alMenu(new Nodo(elegida, null, null)), 0));

            if (!Abierto(colección.IsExpandedInTree, filtro)) continue;
            AgregarRequests(colección.Requests, colección, null, filtro, 1);
            foreach (var carpeta in colección.Folders) AgregarCarpeta(carpeta, colección, filtro, 1);
        }

        if (!hubo) _lista.Children.Add(Ui.Parrafo($"Nada coincide con «{filtro}».", Ui.Tenue));

        MarcarSeleccion(_seleccionada);
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
        _lista.Children.Add(Encabezado("▸ " + carpeta.Name, coincidencias, carpeta.IsExpandedInTree,
            () => { elegida.IsExpandedInTree = !elegida.IsExpandedInTree; Redibujar(); },
            () => _alMenu(new Nodo(dueña, elegida, null)), nivel));

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

            var contenido = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{request.Method}  {request.Name}",
                        FontSize = 14,
                        Foreground = Ui.Normal
                    },
                    new TextBlock
                    {
                        Text = request.Url,
                        FontSize = 11,
                        Foreground = Ui.Tenue,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            };

            var boton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                MinHeight = Ui.Toque,
                Padding = new Thickness(12, 10),
                Content = contenido
            };

            var elegida = request;
            boton.Click += (_, _) =>
            {
                MarcarSeleccion(elegida);
                _alElegir(elegida, dueña);
            };

            var grilla = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(boton, 0);
            var menú = BotónMenú(() => _alMenu(new Nodo(dueña, padre, elegida)));
            Grid.SetColumn(menú, 1);
            grilla.Children.Add(boton);
            grilla.Children.Add(menú);

            var marco = new Border
            {
                Background = Ui.Panel,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(nivel * 12, 0, 0, 0),
                Child = grilla
            };
            _filas[request] = marco;
            _lista.Children.Add(marco);
        }
    }

    Control Encabezado(string texto, int cuantas, bool expandido, Action alTocar, Action alMenu,
        int nivel)
    {
        var boton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            MinHeight = 40,
            Padding = new Thickness(4, 8),
            Content = new TextBlock
            {
                Text = $"{(expandido ? "▾" : "▸")} {texto}  ({cuantas})",
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = Ui.Acento
            }
        };
        boton.Click += (_, _) => alTocar();

        var grilla = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(nivel * 12, 4, 0, 0)
        };
        Grid.SetColumn(boton, 0);
        var menú = BotónMenú(alMenu);
        Grid.SetColumn(menú, 1);
        grilla.Children.Add(boton);
        grilla.Children.Add(menú);
        return grilla;
    }

    static Button BotónMenú(Action al)
    {
        var boton = new Button
        {
            Content = "⋯",
            Background = Brushes.Transparent,
            Foreground = Ui.Tenue,
            FontSize = 18,
            MinHeight = Ui.Toque,
            MinWidth = Ui.Toque,
            Padding = new Thickness(0)
        };
        boton.Click += (_, _) => al();
        return boton;
    }
}
