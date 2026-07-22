using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest.Avalonia.Views;

public partial class WorkspaceWindow : Window
{
    MainWindow _main = null!;
    bool _loadingList;

    public WorkspaceWindow() => InitializeComponent();

    public WorkspaceWindow(MainWindow main) : this()
    {
        _main = main;
        Opened += (_, _) => Refresh();
    }

    void Refresh()
    {
        _loadingList = true;
        var items = Storage.ListWorkspaces();
        WorkspaceList.ItemsSource = items;
        WorkspaceList.SelectedItem = items.FirstOrDefault(w =>
            string.Equals(w.Path, Storage.HasCustomWorkspace ? Storage.WorkspaceRoot : "",
                StringComparison.OrdinalIgnoreCase));
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
            var s = GitService.Status(root);
            GitStatusText.Text = s == null
                ? "No se pudo leer el estado del repo."
                : $"⎇ {s.Branch} · {s.Pending} cambio(s) pendiente(s)" +
                  (s.Ahead + s.Behind > 0 ? $" · ↑{s.Ahead} ↓{s.Behind}" : "") +
                  $"\nremote: {s.Remote ?? "(sin configurar)"}";
            RemoteUrlBox.Text = s?.Remote ?? "";
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
        ResultText.Foreground = ok ? Brush.Parse("#A6E3A1") : Brush.Parse("#F38BA8");
        ResultText.IsVisible = true;
    }

    async void WorkspaceList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingList || WorkspaceList.SelectedItem is not WorkspaceRef ws) return;
        var targetPath = string.IsNullOrEmpty(ws.Path) ? null : ws.Path;
        if (string.Equals(Storage.WorkspaceRoot, ws.Path, StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrEmpty(ws.Path) && !Storage.HasCustomWorkspace)) return; // ya activo

        if (await _main.SwitchWorkspace(targetPath)) { ShowResult(true, $"Workspace activo: {ws.Name}"); Refresh(); }
        else Refresh(); // cancelado: revertir selección
    }

    async void ChooseFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Elegir carpeta del workspace"
        });
        var folder = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(folder)) return;
        if (await _main.SwitchWorkspace(folder)) { ShowResult(true, "Workspace agregado y activado."); Refresh(); }
    }

    async void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceList.SelectedItem is not WorkspaceRef ws || string.IsNullOrEmpty(ws.Path))
        {
            ShowResult(false, "El workspace Personal no se puede quitar.");
            return;
        }
        if (await Dialogs.Confirm(this,
            $"¿Quitar «{ws.Name}» del listado? La carpeta y sus archivos NO se borran.", "Quitar workspace") != DialogResult.Yes)
            return;
        var wasActive = string.Equals(Storage.WorkspaceRoot, ws.Path, StringComparison.OrdinalIgnoreCase);
        Storage.RemoveWorkspace(ws.Path);
        if (wasActive) await _main.SwitchWorkspace(null);
        ShowResult(true, "Workspace quitado del listado.");
        Refresh();
    }

    async void Clone_Click(object? sender, RoutedEventArgs e)
    {
        var url = CloneUrlBox.Text?.Trim() ?? "";
        if (url.Length == 0) { ShowResult(false, "Ingresá la URL del repo a clonar."); return; }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Carpeta destino (vacía)" });
        var folder = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(folder)) return;
        if (Directory.EnumerateFileSystemEntries(folder).Any()) { ShowResult(false, "La carpeta destino tiene que estar vacía."); return; }

        SyncBtn.IsEnabled = false;
        ShowResult(true, "Clonando…");
        var result = await Task.Run(() => GitService.Clone(url, folder));
        if (!result.Ok) { ShowResult(false, result.Message); Refresh(); return; }
        if (await _main.SwitchWorkspace(folder)) { ShowResult(true, "Repositorio clonado y workspace cargado."); Refresh(); }
    }

    void Init_Click(object? sender, RoutedEventArgs e)
    {
        var r = GitService.Init(Storage.WorkspaceRoot);
        ShowResult(r.Ok, r.Message);
        Refresh();
        _main.RefreshGitStatus();
    }

    void SetRemote_Click(object? sender, RoutedEventArgs e)
    {
        var url = RemoteUrlBox.Text?.Trim() ?? "";
        if (url.Length == 0) { ShowResult(false, "Ingresá la URL del remote."); return; }
        if (!GitService.IsRepo(Storage.WorkspaceRoot)) { ShowResult(false, "Primero inicializá el repositorio git."); return; }
        var r = GitService.SetRemote(Storage.WorkspaceRoot, url);
        ShowResult(r.Ok, r.Message);
        Refresh();
    }

    async void Sync_Click(object? sender, RoutedEventArgs e)
    {
        _main.SaveAllForSync();
        SyncBtn.IsEnabled = false;
        ShowResult(true, "Sincronizando…");
        var r = await _main.SyncWorkspaceInteractive(this);
        ShowResult(r.Ok, r.Message);
        SyncBtn.IsEnabled = true;
        Refresh();
        _main.RefreshGitStatus();
    }

    void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
