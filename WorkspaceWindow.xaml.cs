using System.IO;
using System.Windows;
using System.Windows.Media;
using EasyRest.Services;

namespace EasyRest;

public partial class WorkspaceWindow : Window
{
    readonly MainWindow _main;

    public WorkspaceWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        Refresh();
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

    void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Elegir carpeta del workspace" };
        if (dlg.ShowDialog(this) != true) return;

        var folder = dlg.FolderName;
        var collectionsDir = Path.Combine(folder, "collections");
        var hasCollections = Directory.Exists(collectionsDir) &&
                             Directory.GetFiles(collectionsDir, "*.json").Length > 0;

        var question = hasCollections
            ? "La carpeta ya tiene colecciones: se van a cargar y reemplazan lo que ves ahora (tus colecciones actuales quedan en la carpeta anterior). ¿Continuar?"
            : "La carpeta no tiene colecciones: se van a exportar las colecciones actuales ahí. ¿Continuar?";
        if (MessageBox.Show(this, question, "Cambiar workspace", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        if (_main.SwitchWorkspace(folder))
        {
            ShowResult(true, hasCollections ? "Workspace cargado." : "Colecciones exportadas al workspace.");
            Refresh();
        }
    }

    void UseDefault_Click(object sender, RoutedEventArgs e)
    {
        if (!Storage.HasCustomWorkspace) return;
        if (MessageBox.Show(this, "¿Volver a la carpeta por defecto (AppData)? Se cargan las colecciones que haya ahí.",
                "Cambiar workspace", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (_main.SwitchWorkspace(null))
        {
            ShowResult(true, "Workspace por defecto restaurado.");
            Refresh();
        }
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
