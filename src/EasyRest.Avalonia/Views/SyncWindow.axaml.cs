using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using EasyRest.Services;
using EasyRest.Services.Sync;

namespace EasyRest.Avalonia.Views;

/// <summary>Pantalla rápida de cambios y sincronización del workspace activo. No administra workspaces:
/// eso vive en el selector del header («Administrar workspaces…»).</summary>
public partial class SyncWindow : Window
{
    readonly MainWindow _main = null!;
    bool _canSync;

    public SyncWindow() => InitializeComponent();

    public SyncWindow(MainWindow main) : this()
    {
        _main = main;
        Opened += async (_, _) => await Refresh();
    }

    async System.Threading.Tasks.Task Refresh()
    {
        WsName.Text = Storage.ActiveWorkspaceName;

        // Un workspace puede sincronizar con el servidor de sync o con un repo git, y esta
        // pantalla sólo sabía de git: contra un workspace del servidor decía "todavía no es un
        // repositorio git" y encima dejaba el botón apagado — justo en el caso donde forzar una
        // sincronización es lo más útil. La acción de sincronizar ya resolvía bien el backend;
        // lo que estaba cableado a git era lo que se mostraba.
        var atadura = SyncBinding.Load(Storage.SyncBindingFile);
        ServerBtn.IsVisible = atadura.IsSet;
        if (atadura.IsSet)
        {
            await RefreshServidor(atadura);
            return;
        }

        if (!GitService.IsAvailable())
        {
            SetState(false, "git no está disponible en el PATH. Instalá git para sincronizar el workspace.");
            return;
        }

        var root = Storage.WorkspaceRoot;
        var (isRepo, status, changes) = await System.Threading.Tasks.Task.Run(() =>
        {
            var repo = GitService.IsRepo(root);
            return (repo, repo ? GitService.Status(root) : null, repo ? GitService.Changes(root) : new());
        });

        if (!isRepo)
        {
            SetState(false, "Este workspace todavía no es un repositorio git.\n" +
                            "Inicializalo o conectalo desde «Administrar workspaces…» en el selector de arriba.");
            return;
        }

        BranchInfo.Text = status == null
            ? "No se pudo leer el estado del repo."
            : $"⎇ {status.Branch}" +
              (status.Ahead > 0 ? $"  ↑{status.Ahead}" : "") +
              (status.Behind > 0 ? $"  ↓{status.Behind}" : "") +
              $"\nremote: {status.Remote ?? "(sin configurar)"}";

        ChangesList.ItemsSource = changes;
        ChangesCount.Text = changes.Count == 1 ? "1 archivo" : $"{changes.Count} archivos";
        var empty = changes.Count == 0;
        EmptyHint.IsVisible = empty;
        EmptyHint.Text = "No hay cambios locales para sincronizar.";
        ChangesList.IsVisible = !empty;

        SetState(true, null);
    }

    /// <summary>El workspace sincroniza contra el servidor: se muestra a qué servidor y a qué
    /// workspace remoto está atado, y cuánto falta subir.</summary>
    async System.Threading.Tasks.Task RefreshServidor(SyncBinding atadura)
    {
        var cabecera = $"☁ {atadura.WorkspaceName}\nservidor: {atadura.ServerUrl}";

        var sync = WorkspaceSyncResolver.For(Storage.WorkspaceRoot, Storage.SyncBindingFile,
            Storage.SyncStateFile);
        if (sync == null)
        {
            // hay atadura pero no sesión: no es "no sincroniza", es "volvé a entrar"
            SetState(false, cabecera + "\n\nLa sesión con el servidor venció. Reconectate con " +
                            "«Servidor de sync…» y volvé a sincronizar.");
            return;
        }

        var estado = await sync.StatusAsync();
        BranchInfo.Text = cabecera;

        // el backend remoto informa cuántos archivos hay pendientes, no cuáles: la lista de
        // archivos es propia de git. Mostrar el número es honesto; inventar una lista, no.
        ChangesList.ItemsSource = null;
        ChangesList.IsVisible = false;
        var pendientes = estado?.PendingChanges ?? 0;
        ChangesCount.Text = pendientes == 1 ? "1 archivo" : $"{pendientes} archivos";
        EmptyHint.IsVisible = true;
        EmptyHint.Text = pendientes == 0
            ? "Todo lo local ya está en el servidor."
            : $"{pendientes} archivo(s) con cambios locales sin subir.";

        SetState(true, null);
    }

    void SetState(bool canSync, string? message)
    {
        _canSync = canSync;
        SyncBtn.IsEnabled = canSync;
        if (message != null)
        {
            BranchInfo.Text = message;
            ChangesList.ItemsSource = null;
            ChangesCount.Text = "";
            EmptyHint.IsVisible = false;
            ChangesList.IsVisible = false;
        }
    }

    void ShowResult(bool ok, string message)
    {
        ResultText.Text = message;
        ResultText.Foreground = ok ? Brush.Parse("#A6E3A1") : Brush.Parse("#F38BA8");
        ResultText.IsVisible = true;
    }

    async void Sync_Click(object? sender, RoutedEventArgs e)
    {
        if (!_canSync) return;
        _main.SaveAllForSync();
        SyncBtn.IsEnabled = false;
        ShowResult(true, "Sincronizando…");
        var r = await _main.SyncWorkspaceInteractive(this);
        ShowResult(r.Ok, r.Message);
        await Refresh();
        _main.RefreshGitStatus();
    }

    /// <summary>Sólo visible cuando el workspace está atado a un servidor: es donde se reconecta
    /// la sesión vencida, que es el único motivo por el que este panel no puede sincronizar.</summary>
    async void Server_Click(object? sender, RoutedEventArgs e)
    {
        await new SyncServerWindow(true).ShowDialog(this);
        await Refresh();
        _main.RefreshGitStatus();
    }

    void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
