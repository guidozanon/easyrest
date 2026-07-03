using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using EasyRest.Services;

namespace EasyRest.Avalonia.Views;

public partial class WorkspaceWindow : Window
{
    MainWindow _main = null!;

    public WorkspaceWindow() => InitializeComponent();

    public WorkspaceWindow(MainWindow main) : this()
    {
        _main = main;
        Opened += (_, _) => Refresh();
    }

    void Refresh()
    {
        PathBox.Text = Storage.HasCustomWorkspace ? Storage.WorkspaceRoot : $"{Storage.AppDataRoot}  (por defecto)";

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

    async void ChooseFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Elegir carpeta del workspace"
        });
        var folder = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(folder)) return;

        var collectionsDir = Path.Combine(folder, "collections");
        var hasCollections = Directory.Exists(collectionsDir) && Directory.GetFiles(collectionsDir, "*.json").Length > 0;
        var question = hasCollections
            ? "La carpeta ya tiene colecciones: se van a cargar (tus colecciones actuales quedan en la carpeta anterior). ¿Continuar?"
            : "La carpeta no tiene colecciones: se van a exportar las colecciones actuales ahí. ¿Continuar?";
        if (await Dialogs.Confirm(this, question, "Cambiar workspace") != DialogResult.Yes) return;

        if (await _main.SwitchWorkspace(folder))
        {
            ShowResult(true, hasCollections ? "Workspace cargado." : "Colecciones exportadas al workspace.");
            Refresh();
        }
    }

    async void UseDefault_Click(object? sender, RoutedEventArgs e)
    {
        if (!Storage.HasCustomWorkspace) return;
        if (await Dialogs.Confirm(this, "¿Volver a la carpeta por defecto (AppData)?", "Cambiar workspace") != DialogResult.Yes) return;
        if (await _main.SwitchWorkspace(null)) { ShowResult(true, "Workspace por defecto restaurado."); Refresh(); }
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
        var r = await Task.Run(() => GitService.Sync(Storage.WorkspaceRoot));
        ShowResult(r.Ok, r.Message);
        SyncBtn.IsEnabled = true;
        Refresh();
        _main.RefreshGitStatus();
    }

    void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
