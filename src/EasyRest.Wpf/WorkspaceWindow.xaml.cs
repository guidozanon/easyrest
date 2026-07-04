using System.IO;
using System.Windows;
using System.Windows.Media;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest;

public partial class WorkspaceWindow : Window
{
    readonly MainWindow _main;
    bool _loadingList;

    public WorkspaceWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Refresh();
    }

    void Refresh()
    {
        _loadingList = true;
        var items = Storage.ListWorkspaces();
        WorkspaceList.ItemsSource = items;
        var activePath = Storage.HasCustomWorkspace ? Storage.WorkspaceRoot : "";
        WorkspaceList.SelectedItem = items.FirstOrDefault(w =>
            string.Equals(w.Path, activePath, StringComparison.OrdinalIgnoreCase));
        _loadingList = false;

        if (!GitService.IsAvailable())
        {
            GitStatusText.Text = "git no está disponible en el PATH.";
            InitBtn.IsEnabled = SyncBtn.IsEnabled = false;
            return;
        }

        var root = Storage.WorkspaceRoot;
        if (GitService.IsRepo(root))
        {
            var status = GitService.Status(root);
            GitStatusText.Text = status == null
                ? "No se pudo leer el estado del repo."
                : $"⎇ {status.Branch} · {status.Pending} cambio(s) pendiente(s)" +
                  (status.Ahead + status.Behind > 0 ? $" · ↑{status.Ahead} ↓{status.Behind}" : "") +
                  $"\nremote: {status.Remote ?? "(sin configurar)"}";
            RemoteUrlBox.Text = status?.Remote ?? "";
            InitBtn.IsEnabled = false;
            SyncBtn.IsEnabled = true;
        }
        else
        {
            GitStatusText.Text = "La carpeta del workspace no es un repositorio git.";
            InitBtn.IsEnabled = true;
            SyncBtn.IsEnabled = false;
        }
    }

    void ShowResult(bool ok, string message)
    {
        ResultText.Text = message;
        ResultText.Foreground = ok ? (Brush)FindResource("GreenBrush") : (Brush)FindResource("RedBrush");
        ResultText.Visibility = Visibility.Visible;
    }

    void WorkspaceList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingList || WorkspaceList.SelectedItem is not WorkspaceRef ws) return;
        var activePath = Storage.HasCustomWorkspace ? Storage.WorkspaceRoot : "";
        if (string.Equals(ws.Path, activePath, StringComparison.OrdinalIgnoreCase)) return; // ya activo

        if (_main.SwitchWorkspace(string.IsNullOrEmpty(ws.Path) ? null : ws.Path))
        {
            ShowResult(true, $"Workspace activo: {ws.Name}");
            Refresh();
        }
        else Refresh(); // cancelado: revertir selección
    }

    void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Elegir carpeta del workspace" };
        if (dlg.ShowDialog(this) != true) return;
        if (_main.SwitchWorkspace(dlg.FolderName))
        {
            ShowResult(true, "Workspace agregado y activado.");
            Refresh();
        }
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceList.SelectedItem is not WorkspaceRef ws || string.IsNullOrEmpty(ws.Path))
        {
            ShowResult(false, "El workspace Personal no se puede quitar.");
            return;
        }
        if (MessageBox.Show(this, $"¿Quitar «{ws.Name}» del listado? La carpeta y sus archivos NO se borran.",
                "Quitar workspace", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var wasActive = string.Equals(Storage.WorkspaceRoot, ws.Path, StringComparison.OrdinalIgnoreCase);
        Storage.RemoveWorkspace(ws.Path);
        if (wasActive) _main.SwitchWorkspace(null);
        ShowResult(true, "Workspace quitado del listado.");
        Refresh();
    }

    async void Clone_Click(object sender, RoutedEventArgs e)
    {
        var url = CloneUrlBox.Text.Trim();
        if (url.Length == 0)
        {
            ShowResult(false, "Ingresá la URL del repo a clonar.");
            return;
        }
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta destino del clone (vacía)" };
        if (dlg.ShowDialog(this) != true) return;
        var folder = dlg.FolderName;
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        {
            ShowResult(false, "La carpeta destino tiene que estar vacía.");
            return;
        }

        SyncBtn.IsEnabled = false;
        ShowResult(true, "Clonando…");
        var result = await Task.Run(() => GitService.Clone(url, folder));
        if (!result.Ok)
        {
            ShowResult(false, result.Message);
            Refresh();
            return;
        }
        if (_main.SwitchWorkspace(folder))
        {
            ShowResult(true, "Repositorio clonado y workspace cargado.");
            Refresh();
        }
    }

    void Init_Click(object sender, RoutedEventArgs e)
    {
        var result = GitService.Init(Storage.WorkspaceRoot);
        ShowResult(result.Ok, result.Message);
        Refresh();
        _main.RefreshGitStatus();
    }

    void SetRemote_Click(object sender, RoutedEventArgs e)
    {
        var url = RemoteUrlBox.Text.Trim();
        if (url.Length == 0)
        {
            ShowResult(false, "Ingresá la URL del remote.");
            return;
        }
        if (!GitService.IsRepo(Storage.WorkspaceRoot))
        {
            ShowResult(false, "Primero inicializá el repositorio git.");
            return;
        }
        var result = GitService.SetRemote(Storage.WorkspaceRoot, url);
        ShowResult(result.Ok, result.Message);
        Refresh();
    }

    async void Sync_Click(object sender, RoutedEventArgs e)
    {
        _main.SaveAllForSync();
        SyncBtn.IsEnabled = false;
        ShowResult(true, "Sincronizando…");
        var result = await Task.Run(() => GitService.Sync(Storage.WorkspaceRoot));
        ShowResult(result.Ok, result.Message);
        SyncBtn.IsEnabled = true;
        Refresh();
        _main.RefreshGitStatus();
    }
}
