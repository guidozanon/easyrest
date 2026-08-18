using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest.Services;
using EasyRest.Services.Sync;

using Button = Avalonia.Controls.Button;
using Orientation = Avalonia.Layout.Orientation;

namespace EasyRest.Android;

/// <summary>Conectar el teléfono a un servidor de sync y elegir de qué workspace bajar.
///
/// Es la versión móvil de SyncServerWindow del escritorio, con lo mínimo: acá no se administran
/// miembros ni se crean workspaces. Invitar gente desde el colectivo no es un caso real, y toda
/// esa pantalla en un teléfono sería peor que abrir la app de escritorio.</summary>
public class SyncSetupView : UserControl
{
    readonly ShellView _shell;
    readonly TextBox _url = Ui.Campo("", "https://sync.tu-empresa.com", mono: true);
    readonly StackPanel _pila = new() { Margin = new Thickness(16, 16, 16, 24), Spacing = 12 };
    readonly TextBlock _estado = Ui.Nota("");

    SyncConnection? _conexión;

    public SyncSetupView(ShellView shell)
    {
        _shell = shell;

        var binding = SyncBinding.Load(Storage.SyncBindingFile);
        if (binding.IsSet) _url.Text = binding.ServerUrl;

        Content = new ScrollViewer { Content = _pila };
        Dibujar();

        if (binding.IsSet) _ = RestaurarAsync(binding);
    }

    void Dibujar(params Control[] extra)
    {
        _pila.Children.Clear();
        _pila.Children.Add(Ui.Rotulo("Dirección del servidor de tu organización"));
        _pila.Children.Add(_url);
        _pila.Children.Add(Ui.PrimarioAsync("Conectar", Iconos.Nube, ConectarAsync));
        foreach (var control in extra) _pila.Children.Add(control);
        _pila.Children.Add(_estado);
    }

    async Task RestaurarAsync(SyncBinding binding)
    {
        _conexión = SyncConnection.Restore(binding.ServerUrl);
        if (_conexión == null)
        {
            _estado.Text = "La sesión venció. Tocá «Conectar» para volver a entrar.";
            return;
        }
        await MostrarWorkspacesAsync();
    }

    async Task ConectarAsync()
    {
        var url = (_url.Text ?? "").Trim();
        if (url.Length == 0) { _estado.Text = "Escribí la dirección del servidor."; return; }
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;

        // si ya hay sesión guardada para ese server no hace falta pasar por el navegador
        if (SyncConnection.Restore(url) is { } guardada)
        {
            _conexión = guardada;
            await MostrarWorkspacesAsync();
            return;
        }

        _estado.Text = "Preguntando al servidor…";
        try
        {
            using var api = new SyncApiClient(url);
            var meta = await api.GetMetaAsync();

            var botones = new StackPanel { Spacing = 8 };
            foreach (var proveedor in meta.Auth.Providers)
            {
                var id = proveedor.Id;
                var boton = Ui.SecundarioAsync(proveedor.DisplayName, Iconos.Llave,
                    () => LoguearAsync(url, id));
                boton.HorizontalAlignment = HorizontalAlignment.Stretch;
                botones.Children.Add(boton);
            }

            Dibujar(Ui.Rotulo("Iniciá sesión con"), botones);
            _estado.Text = meta.Server;
        }
        catch (Exception ex)
        {
            _estado.Text = $"No se pudo conectar: {ex.Message}";
        }
    }

    async Task LoguearAsync(string url, string providerId)
    {
        _estado.Text = "Abriendo el navegador…";
        try
        {
            using var api = new SyncApiClient(url);
            var contexto = global::Android.App.Application.Context;
            var session = await AndroidLogin.RunAsync(contexto, api, providerId);

            _conexión?.Dispose();
            _conexión = SyncConnection.Establish(url, session);
            await MostrarWorkspacesAsync();
        }
        catch (OperationCanceledException)
        {
            _estado.Text = "Login cancelado.";
        }
        catch (Exception ex)
        {
            _estado.Text = $"El login falló: {ex.Message}";
        }
    }

    async Task MostrarWorkspacesAsync()
    {
        _estado.Text = "Cargando workspaces…";
        try
        {
            var workspaces = await _conexión!.Api.GetWorkspacesAsync();
            var actual = SyncBinding.Load(Storage.SyncBindingFile);

            var lista = new StackPanel { Spacing = 8 };
            foreach (var ws in workspaces)
            {
                var elegido = ws;
                lista.Children.Add(Workspace(ws.Name, ws.Role, ws.Id == actual.WorkspaceId,
                    () => Atar(elegido)));
            }

            if (workspaces.Length == 0)
                lista.Children.Add(Ui.Parrafo(
                    "No sos parte de ningún workspace todavía. Pedí una invitación y aceptala desde " +
                    "la app de escritorio.", Ui.Tenue, 12));

            Dibujar(
                Ui.Tarjeta(6,
                    Ui.Parrafo(_conexión.Account.DisplayName, Ui.Normal),
                    Ui.Nota(_conexión.Account.Email)),
                Ui.Rotulo("Elegí de qué workspace bajar las colecciones"),
                lista,
                Ui.Fantasma("Cerrar sesión", null, CerrarSesión));

            _estado.Text = "";
        }
        catch (Exception ex)
        {
            _estado.Text = $"No se pudieron cargar: {ex.Message}";
        }
    }

    /// <summary>Un workspace como fila elegible: el atado ahora va marcado con el punto y el
    /// borde de acento, que es lo que dice de dónde están bajando las colecciones.</summary>
    static Control Workspace(string nombre, string rol, bool actual, Action al)
    {
        var textos = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = nombre,
                    FontSize = 14,
                    FontWeight = actual ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = actual ? Ui.Acento : Ui.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                Ui.Nota(rol)
            }
        };

        var contenido = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(999),
                    Background = actual ? Ui.Verde : Ui.Superficie,
                    VerticalAlignment = VerticalAlignment.Center
                },
                textos
            }
        };

        var boton = new Button
        {
            Content = contenido,
            Background = actual ? Ui.Tinte(Ui.CAcento, 0.10) : Ui.Panel,
            BorderBrush = actual ? Ui.Acento : Ui.Superficie,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10),
            MinHeight = 60,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        boton.Click += (_, _) => al();
        return boton;
    }

    void Atar(SyncWorkspace ws)
    {
        new SyncBinding
        {
            ServerUrl = _conexión!.Account.ServerUrl,
            WorkspaceId = ws.Id,
            WorkspaceName = ws.Name
        }.Save(Storage.SyncBindingFile);

        _ = _shell.VolverYSincronizarAsync();
    }

    void CerrarSesión()
    {
        _conexión?.Forget();
        _conexión?.Dispose();
        _conexión = null;
        SyncBinding.Clear(Storage.SyncBindingFile);
        Dibujar();
        _estado.Text = "Sesión cerrada. Las colecciones que ya bajaron quedan en el teléfono.";
    }
}
