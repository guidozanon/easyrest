using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using EasyRest.Services;

namespace EasyRest.Avalonia.Views;

/// <summary>Panel de actualizaciones: chequea la última release de GitHub, baja el zip de la
/// plataforma y lo aplica reiniciando la app.</summary>
public partial class UpdateWindow : Window
{
    enum Mode { None, Install, Restart, OpenGitHub }

    readonly UpdateInfo? _preloaded;
    readonly CancellationTokenSource _cts = new();
    UpdateInfo? _info;
    Mode _mode = Mode.None;
    string? _downloadedZip;
    bool _loadingPrefs;
    bool _busy;

    /// <summary>Última release consultada (la que se mostró), para que el shell refresque su aviso.</summary>
    public UpdateInfo? LastCheck => _info;

    public UpdateWindow() : this(null) { }

    /// <summary>preloaded: resultado de un chequeo previo (el de arranque), para no repetirlo.</summary>
    public UpdateWindow(UpdateInfo? preloaded)
    {
        InitializeComponent();
        _preloaded = preloaded;

        _loadingPrefs = true;
        AutoCheck.IsChecked = Storage.CheckUpdatesOnStartup;
        _loadingPrefs = false;

        Opened += async (_, _) => await StartAsync();
        Closing += (_, _) => _cts.Cancel();
    }

    async Task StartAsync()
    {
        if (_preloaded != null) { ShowRelease(_preloaded); return; }
        await CheckAsync();
    }

    async Task CheckAsync()
    {
        TitleText.Text = "Buscando actualizaciones…";
        TitleText.Foreground = Brush.Parse("#CDD6F4");
        SubText.Text = $"Versión instalada: v{UpdateService.CurrentVersion}";
        Progress.IsIndeterminate = true;
        Progress.IsVisible = true;
        SetButtons(action: null, skip: false);

        try
        {
            var info = await UpdateService.CheckAsync(_cts.Token);
            Storage.LastUpdateCheckUtc = DateTime.UtcNow;
            ShowRelease(info);
        }
        catch (OperationCanceledException)
        {
            // la ventana se cerró: no hay nada que mostrar
        }
        catch (Exception ex)
        {
            Progress.IsVisible = false;
            TitleText.Text = "✖ No se pudo consultar las actualizaciones";
            TitleText.Foreground = Brush.Parse("#F38BA8");
            SubText.Text = $"Versión instalada: v{UpdateService.CurrentVersion}";
            ShowStatus(ex.Message, "#F38BA8");
            SetButtons(action: null, skip: false);
        }
    }

    void ShowRelease(UpdateInfo info)
    {
        _info = info;
        Progress.IsVisible = false;
        Progress.IsIndeterminate = false;
        StatusText.IsVisible = false;

        if (!info.IsNewer)
        {
            TitleText.Text = "✔ EasyRest está al día";
            TitleText.Foreground = Brush.Parse("#A6E3A1");
            SubText.Text = $"Versión instalada: v{info.CurrentVersion}";
            NotesBox.IsVisible = false;
            SetButtons(action: null, skip: false);
            return;
        }

        TitleText.Text = $"Hay una versión nueva: v{info.Version}";
        TitleText.Foreground = Brush.Parse("#89B4FA");
        SubText.Text = $"Tenés instalada la v{info.CurrentVersion}." +
                       (info.AssetSize > 0 ? $"  Descarga: {FormatSize(info.AssetSize)}." : "");

        NotesText.Text = string.IsNullOrWhiteSpace(info.Notes)
            ? "(La release no trae notas.)"
            : info.Notes;
        NotesBox.IsVisible = true;

        if (info.CanInstall)
        {
            SetButtons(action: "Descargar e instalar", skip: true);
            _mode = Mode.Install;
        }
        else
        {
            // Linux (el CI no publica binarios), `dotnet run` o un install viejo de un único
            // .exe: se descarga a mano. El botón de acción ya lleva a GitHub, así que el otro sobra.
            SetButtons(action: "Descargar desde GitHub", skip: true, github: false);
            _mode = Mode.OpenGitHub;
            var reason = UpdateService.AssetSuffix == null
                ? "No hay binarios publicados para esta plataforma: actualizá desde el código o el release."
                : UpdateService.InstallTarget?.Kind == InstallKind.WindowsLegacySingleFile
                    ? "Esta instalación es la vieja de un único .exe: bajá el installer (EasyRest-Setup) " +
                      "una vez y después vuelve a actualizarse sola."
                    : "Esta instalación no se puede reemplazar sola (¿la estás corriendo desde el código?).";
            ShowStatus(reason, "#F9E2AF");
        }
    }

    // ----- Acciones -----

    async void Action_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy || _info == null) return;

        switch (_mode)
        {
            case Mode.OpenGitHub:
                await OpenRelease();
                return;
            case Mode.Restart:
                if (_downloadedZip != null) await ApplyAsync(_downloadedZip);
                return;
            case Mode.Install:
                await DownloadAsync();
                return;
        }
    }

    async Task DownloadAsync()
    {
        if (_info == null) return;
        _busy = true;
        SetButtons(action: null, skip: false);
        ActionBtn.IsVisible = true;
        ActionBtn.IsEnabled = false;
        ActionBtn.Content = "Descargando…";
        Progress.IsIndeterminate = false;
        Progress.Value = 0;
        Progress.IsVisible = true;
        ShowStatus("Descargando la actualización…", "#A6ADC8");

        try
        {
            // System.Progress<> calificado: "Progress" también es el nombre del ProgressBar
            var progress = new System.Progress<double>(p =>
                Dispatcher.UIThread.Post(() => Progress.Value = p));
            var zip = await UpdateService.DownloadAsync(_info, progress, _cts.Token);
            _downloadedZip = zip;
            Progress.IsVisible = false;
            _busy = false;
            await ApplyAsync(zip);
        }
        catch (OperationCanceledException)
        {
            _busy = false;
        }
        catch (Exception ex)
        {
            _busy = false;
            Progress.IsVisible = false;
            ShowStatus("No se pudo descargar la actualización: " + ex.Message, "#F38BA8");
            SetButtons(action: "Reintentar", skip: true);
            _mode = Mode.Install;
        }
    }

    /// <summary>Prepara el reemplazo y cierra la app: el script externo espera a que cierre,
    /// pisa los binarios y vuelve a abrir EasyRest.</summary>
    async Task ApplyAsync(string zip)
    {
        var confirm = await Dialogs.Confirm(this,
            $"La versión v{_info?.Version} está lista.\n\n" +
            "EasyRest se va a cerrar para instalarla y se va a abrir de nuevo solo. " +
            "Los cambios pendientes se guardan antes de salir.\n\n¿Instalar ahora?",
            "Instalar actualización", withCancel: false);

        if (confirm != DialogResult.Yes)
        {
            ShowStatus("La actualización quedó descargada: podés instalarla cuando quieras.", "#F9E2AF");
            SetButtons(action: "Instalar y reiniciar", skip: true);
            _mode = Mode.Restart;
            return;
        }

        try
        {
            UpdateService.ApplyAndRestart(zip);
        }
        catch (Exception ex)
        {
            ShowStatus("No se pudo instalar la actualización: " + ex.Message, "#F38BA8");
            SetButtons(action: "Ver en GitHub", skip: true);
            _mode = Mode.OpenGitHub;
            return;
        }

        // guardar todo antes de salir (el cierre normal también guarda, pero acá evitamos
        // que un diálogo de "cambios sin guardar" frene el reinicio)
        if (global::Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow main } life)
        {
            main.SaveAllForSync();
            Close();
            life.Shutdown();
        }
        else
        {
            Close();
            Environment.Exit(0);
        }
    }

    async void Skip_Click(object? sender, RoutedEventArgs e)
    {
        if (_info == null) return;
        Storage.SkippedUpdateVersion = _info.Version;
        await Dialogs.Info(this,
            $"No se va a volver a avisar de la v{_info.Version}.\n" +
            "Podés buscar actualizaciones cuando quieras desde el menú ⋯ del sidebar.",
            "Versión omitida");
        Close();
    }

    async void GitHub_Click(object? sender, RoutedEventArgs e) => await OpenRelease();

    async Task OpenRelease()
    {
        var url = _info?.ReleaseUrl;
        if (string.IsNullOrWhiteSpace(url)) url = UpdateService.RepoUrl + "/releases/latest";
        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
            await launcher.LaunchUriAsync(new Uri(url!));
    }

    void AutoCheck_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loadingPrefs) return;
        Storage.CheckUpdatesOnStartup = AutoCheck.IsChecked == true;
    }

    void Close_Click(object? sender, RoutedEventArgs e) => Close();

    // ----- Helpers de UI -----

    void SetButtons(string? action, bool skip, bool github = true)
    {
        ActionBtn.IsVisible = action != null;
        ActionBtn.IsEnabled = true;
        if (action != null) ActionBtn.Content = action;
        SkipBtn.IsVisible = skip;
        GitHubBtn.IsVisible = github;
    }

    void ShowStatus(string message, string color)
    {
        StatusText.Text = message;
        StatusText.Foreground = Brush.Parse(color);
        StatusText.IsVisible = true;
    }

    static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#} MB" : $"{bytes / 1024d:0} KB";
}
