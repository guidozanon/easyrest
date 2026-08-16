using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyRest.Services;
using EasyRest.Services.Sync;

namespace EasyRest.Avalonia.Views;

/// <summary>Conectar el workspace activo a un servidor de sync: iniciar sesión, elegir contra qué
/// workspace remoto sincroniza y administrar quién más entra.
///
/// Es distinta de SyncWindow, que muestra los cambios pendientes y sincroniza. Acá se configura;
/// allá se ejecuta.</summary>
public partial class SyncServerWindow : Window
{
    /// <summary>Filas de las listas. Records para que el binding del DataTemplate sea directo y no
    /// haya que exponer los tipos de la API a la vista.</summary>
    record FilaWorkspace(SyncWorkspace Ws, string Name, string Detalle);
    record FilaMiembro(SyncMember? Miembro, SyncInvitation? Invitación, string Titulo, string Detalle);

    SyncConnection? _conexión;
    SyncWorkspace? _elegido;

    public SyncServerWindow() => InitializeComponent();

    public SyncServerWindow(bool _) : this()
    {
        Opened += async (_, _) => await RestaurarAsync();
    }

    // ----- Sesión -----

    async Task RestaurarAsync()
    {
        var binding = SyncBinding.Load(Storage.SyncBindingFile);
        if (!binding.IsSet) return;

        ServerUrl.Text = binding.ServerUrl;
        _conexión = SyncConnection.Restore(binding.ServerUrl);
        if (_conexión == null)
        {
            // hay servidor configurado pero se perdió la sesión: no es lo mismo que no tener nada
            Aviso("La sesión venció. Conectate de nuevo para seguir sincronizando.");
            await ConectarAsync();
            return;
        }

        MostrarSesión();
        await CargarWorkspacesAsync(binding.WorkspaceId);
    }

    async void Conectar_Click(object? sender, RoutedEventArgs e) => await ConectarAsync();

    async Task ConectarAsync()
    {
        var url = (ServerUrl.Text ?? "").Trim();
        if (url.Length == 0) { Aviso("Escribí la dirección del servidor."); return; }
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;

        // si ya hay sesión guardada para ese server, no hay que volver a loguearse
        var guardada = SyncConnection.Restore(url);
        if (guardada != null)
        {
            _conexión = guardada;
            MostrarSesión();
            await CargarWorkspacesAsync(SyncBinding.Load(Storage.SyncBindingFile).WorkspaceId);
            return;
        }

        await Ocupado("Preguntando al servidor…", async () =>
        {
            using var api = new SyncApiClient(url);
            var meta = await api.GetMetaAsync();
            MostrarProveedores(url, meta);
            Aviso($"{meta.Server} · elegí con qué iniciar sesión.");
        });
    }

    void MostrarProveedores(string url, SyncMeta meta)
    {
        ProveedoresPanel.IsVisible = true;
        Proveedores.ItemsSource = meta.Auth.Providers.Select(p =>
        {
            var boton = new Button { Content = p.DisplayName, Margin = new Thickness(0, 0, 8, 0) };
            boton.Click += async (_, _) => await LoguearAsync(url, p.Id);
            return boton;
        }).ToList();
    }

    async Task LoguearAsync(string url, string providerId)
    {
        await Ocupado("Esperando el navegador…", async () =>
        {
            using var api = new SyncApiClient(url);
            var session = await LoopbackLogin.RunAsync(api, providerId);

            _conexión?.Dispose();
            _conexión = SyncConnection.Establish(url, session);

            ProveedoresPanel.IsVisible = false;
            MostrarSesión();
            await CargarWorkspacesAsync(SyncBinding.Load(Storage.SyncBindingFile).WorkspaceId);
            Aviso($"Sesión iniciada como {session.User.Email}.");
        });
    }

    void MostrarSesión()
    {
        var cuenta = _conexión!.Account;
        SesionPanel.IsVisible = true;
        SalirBtn.IsVisible = true;
        WorkspacesPanel.IsVisible = true;
        SesionTexto.Text = $"{cuenta.DisplayName} · {cuenta.Email}" +
                           $"\n{cuenta.ServerUrl} · vía {cuenta.Provider}" +
                           (cuenta.IsServerAdmin ? "\nSos administrador de este servidor." : "");
    }

    async void CerrarSesion_Click(object? sender, RoutedEventArgs e)
    {
        if (_conexión == null) return;

        var confirmar = await Dialogs.Confirm(this,
            "Se va a cerrar la sesión y este workspace va a dejar de sincronizar con el servidor.\n\n" +
            "Los archivos locales quedan como están.",
            "Cerrar sesión");
        if (confirmar != DialogResult.Yes) return;

        _conexión.Forget();
        _conexión.Dispose();
        _conexión = null;
        _elegido = null;
        SyncBinding.Clear(Storage.SyncBindingFile);

        SesionPanel.IsVisible = false;
        SalirBtn.IsVisible = false;
        WorkspacesPanel.IsVisible = false;
        Aviso("Sesión cerrada.");
    }

    // ----- Workspaces remotos -----

    async Task CargarWorkspacesAsync(Guid seleccionar)
    {
        await Ocupado("Cargando workspaces…", async () =>
        {
            var lista = await _conexión!.Api.GetWorkspacesAsync();
            Workspaces.ItemsSource = lista.Select(w => new FilaWorkspace(w, w.Name,
                $"rol: {w.Role}{(w.CanReadSecrets ? " · ve secretos" : "")}")).ToList();

            var fila = ((List<FilaWorkspace>)Workspaces.ItemsSource!)
                .FirstOrDefault(f => f.Ws.Id == seleccionar);
            if (fila != null) Workspaces.SelectedItem = fila;
            Aviso(lista.Length == 0
                ? "Todavía no hay workspaces. Creá uno o aceptá una invitación."
                : null);
        });
    }

    async void Workspaces_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _elegido = (Workspaces.SelectedItem as FilaWorkspace)?.Ws;
        UsarBtn.IsEnabled = _elegido != null;
        if (_elegido != null) await CargarMiembrosAsync();
    }

    void Usar_Click(object? sender, RoutedEventArgs e)
    {
        if (_elegido == null || _conexión == null) return;

        new SyncBinding
        {
            ServerUrl = _conexión.Account.ServerUrl,
            WorkspaceId = _elegido.Id,
            WorkspaceName = _elegido.Name
        }.Save(Storage.SyncBindingFile);

        Aviso($"«{Storage.ActiveWorkspaceName}» ahora sincroniza con «{_elegido.Name}».");
    }

    async void Crear_Click(object? sender, RoutedEventArgs e)
    {
        var nombre = await Dialogs.Prompt(this, "Crear workspace",
            "Nombre del workspace en el servidor:");
        if (string.IsNullOrWhiteSpace(nombre)) return;

        await Ocupado("Creando…", async () =>
        {
            var ws = await _conexión!.Api.CreateWorkspaceAsync(nombre.Trim());
            await CargarWorkspacesAsync(ws.Id);
        });
    }

    async void Aceptar_Click(object? sender, RoutedEventArgs e)
    {
        var token = await Dialogs.Prompt(this, "Aceptar invitación",
            "Pegá el token que te pasaron:");
        if (string.IsNullOrWhiteSpace(token)) return;

        await Ocupado("Aceptando…", async () =>
        {
            var ws = await _conexión!.Api.AcceptInvitationAsync(token.Trim());
            await CargarWorkspacesAsync(ws.Id);
            Aviso($"Ya sos parte de «{ws.Name}».");
        });
    }

    // ----- Miembros e invitaciones -----

    async Task CargarMiembrosAsync()
    {
        if (_elegido == null) return;

        await Ocupado("Cargando miembros…", async () =>
        {
            var filas = new List<FilaMiembro>();

            foreach (var m in await _conexión!.Api.GetMembersAsync(_elegido.Id))
                filas.Add(new FilaMiembro(m, null, $"{m.DisplayName} · {m.Email}",
                    $"{m.Role}{(m.CanReadSecrets ? " · ve secretos" : "")}"));

            // las invitaciones sólo las ve quien puede administrar; para el resto no es un error
            try
            {
                foreach (var i in await _conexión.Api.GetInvitationsAsync(_elegido.Id))
                    if (!i.Accepted)
                        filas.Add(new FilaMiembro(null, i, $"⏳ {i.Email ?? "invitación abierta"}",
                            $"pendiente · {i.Role} · vence {i.ExpiresAt.ToLocalTime():d}"));
            }
            catch (SyncApiException)
            {
                // sin permiso para verlas: se muestran sólo los miembros
            }

            Miembros.ItemsSource = filas;
        });
    }

    void Miembros_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var fila = Miembros.SelectedItem as FilaMiembro;
        RolBtn.IsEnabled = fila?.Miembro != null;
        QuitarBtn.IsEnabled = fila != null;
    }

    async void Invitar_Click(object? sender, RoutedEventArgs e)
    {
        if (_elegido == null) return;

        var email = await Dialogs.Prompt(this, "Invitar",
            "Email de la persona, o dejalo vacío para una invitación abierta que sirva una vez:");
        if (email == null) return;

        var rol = await Dialogs.Choice(this, "Rol", "¿Con qué rol entra?", "Miembro", "Admin");
        if (rol == null) return;

        await Ocupado("Creando la invitación…", async () =>
        {
            var invitación = await _conexión!.Api.CreateInvitationAsync(_elegido.Id,
                string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
                rol == 0 ? "Member" : "Admin");

            // el token en claro se muestra una sola vez: el server sólo guarda el hash
            await Dialogs.Info(this,
                "Pasale este token a la persona. No se puede volver a ver: si se pierde, hay que " +
                "crear otra invitación.\n\n" + invitación.Token,
                "Invitación creada");

            await CargarMiembrosAsync();
        });
    }

    async void Rol_Click(object? sender, RoutedEventArgs e)
    {
        if (_elegido == null || (Miembros.SelectedItem as FilaMiembro)?.Miembro is not { } m) return;

        var rol = await Dialogs.Choice(this, $"Rol de {m.DisplayName}",
            "Admin puede invitar y administrar. Miembro edita. Viewer sólo lee.",
            "Admin", "Miembro", "Viewer");
        if (rol == null) return;

        await Ocupado("Cambiando el rol…", async () =>
        {
            await _conexión!.Api.UpdateMemberAsync(_elegido.Id, m.UserId,
                rol switch { 0 => "Admin", 1 => "Member", _ => "Viewer" });
            await CargarMiembrosAsync();
        });
    }

    async void Quitar_Click(object? sender, RoutedEventArgs e)
    {
        if (_elegido == null || Miembros.SelectedItem is not FilaMiembro fila) return;

        var qué = fila.Miembro != null ? $"a {fila.Miembro.DisplayName}" : "esa invitación";
        var confirmar = await Dialogs.Confirm(this, $"¿Sacar {qué} de «{_elegido.Name}»?", "Quitar");
        if (confirmar != DialogResult.Yes) return;

        await Ocupado("Quitando…", async () =>
        {
            if (fila.Miembro is { } m)
                await _conexión!.Api.RemoveMemberAsync(_elegido.Id, m.UserId);
            else
                await _conexión!.Api.RevokeInvitationAsync(_elegido.Id, fila.Invitación!.Id);
            await CargarMiembrosAsync();
        });
    }

    // ----- Andamiaje -----

    /// <summary>Corre algo que puede tardar o fallar, con el estado a la vista. Los errores de la
    /// API se muestran tal cual: el server ya manda mensajes en castellano y pensados para leer.</summary>
    async Task Ocupado(string mensaje, Func<Task> acción)
    {
        Aviso(mensaje);
        IsEnabled = false;
        try
        {
            await acción();
        }
        catch (SyncApiException ex)
        {
            Aviso(ex.Message);
        }
        catch (OperationCanceledException)
        {
            Aviso("Login cancelado.");
        }
        catch (Exception ex)
        {
            Aviso($"No se pudo: {ex.Message}");
        }
        finally
        {
            IsEnabled = true;
        }
    }

    void Aviso(string? texto) => Estado.Text = texto ?? "";

    void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
