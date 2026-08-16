using System.Text;
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

/// <summary>La app en el teléfono: colecciones, editor de request y la conexión con el servidor
/// de sync.
///
/// Es una sola vista que cambia de contenido en vez de varias actividades: en móvil Avalonia corre
/// sobre una única actividad, y para tres pantallas un navegador propio de dos líneas alcanza.
///
/// Escrita en C# y no en XAML por el bug de build que documenta docs/ANDROID.md.</summary>
public class ShellView : UserControl
{
    static readonly IBrush Fondo = new SolidColorBrush(Color.Parse("#1E1E2E"));
    static readonly IBrush Panel = new SolidColorBrush(Color.Parse("#272739"));
    static readonly IBrush Acento = new SolidColorBrush(Color.Parse("#89B4FA"));
    static readonly IBrush Tenue = new SolidColorBrush(Color.Parse("#9399B2"));
    static readonly IBrush Normal = new SolidColorBrush(Color.Parse("#CDD6F4"));
    static readonly IBrush Verde = new SolidColorBrush(Color.Parse("#A6E3A1"));
    static readonly IBrush Rojo = new SolidColorBrush(Color.Parse("#F38BA8"));

    readonly ContentControl _cuerpo = new();
    readonly TextBlock _titulo = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Acento };
    readonly TextBlock _estado = new() { FontSize = 11, Foreground = Tenue, TextWrapping = TextWrapping.Wrap };
    readonly Button _atrás = new() { Content = "‹", FontSize = 20, IsVisible = false, Padding = new Thickness(10, 0) };

    List<RequestCollection> _colecciones = new();
    EnvironmentModel _ambiente = new() { Name = "Móvil" };

    public ShellView()
    {
        _atrás.Click += (_, _) => MostrarColecciones();

        Content = new DockPanel
        {
            Background = Fondo,
            Children =
            {
                Encabezado(),
                _cuerpo
            }
        };

        Recargar();
        MostrarColecciones();
    }

    Control Encabezado()
    {
        var barra = new StackPanel
        {
            Margin = new Thickness(14, 12),
            Spacing = 6,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { _atrás, _titulo }
                },
                _estado
            }
        };
        DockPanel.SetDock(barra, Dock.Top);
        return barra;
    }

    void Recargar()
    {
        _colecciones = Storage.LoadCollections();
        var ambientes = Storage.LoadEnvironments();
        if (ambientes.Count > 0) _ambiente = ambientes[0];
    }

    // ----- Colecciones -----

    void MostrarColecciones()
    {
        Recargar();
        _titulo.Text = "EasyRest";
        _atrás.IsVisible = false;
        MostrarEstadoDeSync();

        var pila = new StackPanel { Margin = new Thickness(14, 0, 14, 14), Spacing = 10 };

        pila.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                AccionAsync("Sincronizar", SincronizarAsync),
                Accion("Servidor…", MostrarConexión),
                Accion("Diagnóstico", () => Navegar("Diagnóstico", new SpikeView()))
            }
        });

        if (_colecciones.Count == 0)
        {
            pila.Children.Add(Parrafo(
                "Todavía no hay colecciones en este teléfono.\n\n" +
                "Conectate a un servidor de sync y elegí un workspace: las colecciones bajan solas."));
        }
        else
        {
            foreach (var colección in _colecciones)
            {
                pila.Children.Add(new TextBlock
                {
                    Text = colección.Name,
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Acento,
                    Margin = new Thickness(0, 6, 0, 0)
                });
                AgregarRequests(pila, colección.Requests, 0);
                foreach (var carpeta in colección.Folders) AgregarCarpeta(pila, carpeta, 1);
            }
        }

        _cuerpo.Content = new ScrollViewer { Content = pila };
    }

    void AgregarCarpeta(StackPanel pila, Folder carpeta, int nivel)
    {
        pila.Children.Add(new TextBlock
        {
            Text = "▸ " + carpeta.Name,
            FontSize = 13,
            Foreground = Tenue,
            Margin = new Thickness(nivel * 14, 4, 0, 0)
        });
        AgregarRequests(pila, carpeta.Requests, nivel);
        foreach (var hija in carpeta.Folders) AgregarCarpeta(pila, hija, nivel + 1);
    }

    void AgregarRequests(StackPanel pila, IEnumerable<RequestItem> requests, int nivel)
    {
        foreach (var request in requests)
        {
            var fila = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Panel,
                Padding = new Thickness(12, 10),
                Margin = new Thickness(nivel * 14, 0, 0, 0),
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{request.Method}  {request.Name}",
                            FontSize = 14,
                            Foreground = Normal
                        },
                        new TextBlock
                        {
                            Text = request.Url,
                            FontSize = 11,
                            Foreground = Tenue,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }
                    }
                }
            };
            var elegida = request;
            fila.Click += (_, _) => Navegar(elegida.Name, new RequestView(elegida, _ambiente));
            pila.Children.Add(fila);
        }
    }

    void Navegar(string título, Control vista)
    {
        _titulo.Text = título;
        _atrás.IsVisible = true;
        _cuerpo.Content = vista;
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
            if (resultado.PulledRemote) MostrarColecciones();
        }
        catch (Exception ex)
        {
            _estado.Text = $"No se pudo sincronizar: {ex.Message}";
        }
    }

    void MostrarConexión() => Navegar("Servidor de sync", new SyncSetupView(this));

    /// <summary>La llama SyncSetupView cuando termina de atar un workspace: hay que volver a la
    /// lista y bajar lo que haya.</summary>
    internal async Task VolverYSincronizarAsync()
    {
        MostrarColecciones();
        await SincronizarAsync();
        MostrarColecciones();
    }

    // ----- Piezas compartidas -----

    internal static Button Accion(string texto, Action al)
    {
        var boton = new Button { Content = texto, Padding = new Thickness(12, 8) };
        boton.Click += (_, _) => al();
        return boton;
    }

    /// <summary>Separada de la sincrónica y no una sobrecarga: un lambda como `() => AlgoAsync()`
    /// encaja en Action y en Func&lt;Task&gt;, y el compilador no sabe cuál querés.</summary>
    internal static Button AccionAsync(string texto, Func<Task> al)
    {
        var boton = new Button { Content = texto, Padding = new Thickness(12, 8) };
        boton.Click += async (_, _) => await al();
        return boton;
    }

    internal static TextBlock Parrafo(string texto, IBrush? color = null, double tamaño = 13) => new()
    {
        Text = texto,
        FontSize = tamaño,
        Foreground = color ?? Normal,
        TextWrapping = TextWrapping.Wrap
    };

    internal static TextBlock Rotulo(string texto) => new()
    {
        Text = texto,
        FontSize = 11,
        Foreground = Tenue
    };

    internal static Border Tarjeta(params Control[] hijos)
    {
        var pila = new StackPanel { Spacing = 8 };
        foreach (var hijo in hijos) pila.Children.Add(hijo);
        return new Border
        {
            Background = Panel,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = pila
        };
    }

    internal static IBrush ColorOk => Verde;
    internal static IBrush ColorError => Rojo;
    internal static IBrush ColorTenue => Tenue;
    internal static IBrush ColorNormal => Normal;
}
