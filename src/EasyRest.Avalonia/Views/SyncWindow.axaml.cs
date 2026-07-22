using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using EasyRest.Services;

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

    void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
