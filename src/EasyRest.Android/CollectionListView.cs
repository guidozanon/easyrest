using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest.Models;

using Button = Avalonia.Controls.Button;

namespace EasyRest.Android;

/// <summary>El árbol de colecciones: el panel izquierdo en tablet y fold desplegado, la pantalla
/// de entrada en teléfono.
///
/// Carpetas plegables y un buscador arriba. El buscador es lo que hace usable un workspace real
/// desde un teléfono: con doscientas requests importadas de un OpenAPI, bajar scrolleando no es
/// una opción, y escribir tres letras sí. Mientras hay filtro se muestra todo lo que coincide sin
/// respetar el plegado, porque esconder un resultado detrás de una carpeta cerrada haría que la
/// búsqueda parezca rota.</summary>
internal class CollectionListView : UserControl
{
    readonly Action<RequestItem, RequestCollection> _alElegir;
    readonly StackPanel _lista = new() { Spacing = 4, Margin = new Thickness(10, 0, 10, 14) };
    readonly TextBox _busqueda;
    readonly Dictionary<RequestItem, Border> _filas = new();

    List<RequestCollection> _colecciones = new();
    RequestItem? _seleccionada;

    public CollectionListView(Action<RequestItem, RequestCollection> alElegir)
    {
        _alElegir = alElegir;

        _busqueda = Ui.Campo("", "Buscar request…");
        _busqueda.Margin = new Thickness(10, 0, 10, 8);
        _busqueda.TextChanged += (_, _) => Redibujar();

        var raíz = new DockPanel();
        DockPanel.SetDock(_busqueda, Dock.Top);
        raíz.Children.Add(_busqueda);
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

    void Redibujar()
    {
        _lista.Children.Clear();
        _filas.Clear();

        var filtro = (_busqueda.Text ?? "").Trim();

        if (_colecciones.Count == 0)
        {
            _lista.Children.Add(Ui.Parrafo(
                "Todavía no hay colecciones en este teléfono.\n\n" +
                "Conectate a un servidor de sync y elegí un workspace: las colecciones bajan solas.",
                Ui.Tenue));
            return;
        }

        var hubo = false;
        foreach (var colección in _colecciones)
        {
            var requests = colección.AllRequests.Count(r => Coincide(r, filtro));
            if (requests == 0 && filtro.Length > 0) continue;
            hubo = true;

            _lista.Children.Add(Encabezado(colección.Name, requests, colección.IsExpandedInTree,
                () => { colección.IsExpandedInTree = !colección.IsExpandedInTree; Redibujar(); }));

            if (!Abierto(colección.IsExpandedInTree, filtro)) continue;
            AgregarRequests(colección.Requests, colección, filtro, 1);
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

        var fila = Encabezado("▸ " + carpeta.Name, coincidencias, carpeta.IsExpandedInTree,
            () => { carpeta.IsExpandedInTree = !carpeta.IsExpandedInTree; Redibujar(); });
        fila.Margin = new Thickness(nivel * 12, 4, 0, 0);
        _lista.Children.Add(fila);

        if (!Abierto(carpeta.IsExpandedInTree, filtro)) return;
        AgregarRequests(carpeta.Requests, dueña, filtro, nivel + 1);
        foreach (var hija in carpeta.Folders) AgregarCarpeta(hija, dueña, filtro, nivel + 1);
    }

    void AgregarRequests(IEnumerable<RequestItem> requests, RequestCollection dueña, string filtro,
        int nivel)
    {
        foreach (var request in requests)
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

            var marco = new Border
            {
                Background = Ui.Panel,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(nivel * 12, 0, 0, 0),
                Child = boton
            };
            _filas[request] = marco;
            _lista.Children.Add(marco);
        }
    }

    Control Encabezado(string texto, int cuantas, bool expandido, Action alTocar)
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
        return boton;
    }
}
