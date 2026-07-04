using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using EasyRest.Avalonia.Views;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest.Avalonia;

public partial class MainWindow : Window
{
    const string NoEnvironment = "(Sin ambiente)";
    const string NewEnvironmentItem = "＋ Nuevo ambiente…";
    const string ManageEnvironmentsItem = "⚙ Administrar ambientes…";
    bool _envComboGuard;
    object? _lastRealEnvItem;

    public ObservableCollection<RequestCollection> Collections { get; } = new();
    public ObservableCollection<EnvironmentModel> Environments { get; } = new();
    public ObservableCollection<object> OpenTabs { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        foreach (var c in Storage.LoadCollections()) Collections.Add(c);
        foreach (var e in Storage.LoadEnvironments()) Environments.Add(e);
        Tree.ItemsSource = Collections;
        RequestTabs.ItemsSource = OpenTabs;
        LogsItems.ItemsSource = ExecutionLog.Entries;
        OpenTabs.CollectionChanged += (_, _) => UpdateTabsVisibility();

        Tree.AddHandler(DragDrop.DragOverEvent, Tree_DragOver);
        Tree.AddHandler(DragDrop.DropEvent, Tree_Drop);

        RefreshEnvCombo();
        var settings = Storage.LoadSettings();
        var activeEnv = Environments.FirstOrDefault(e => e.Id == settings.ActiveEnvironmentId);
        if (activeEnv != null) EnvCombo.SelectedItem = activeEnv;

        UpdateStatusEnv();
        Opened += (_, _) => RefreshGitStatus();
        Closing += OnClosing;
    }

    public EnvironmentModel? ActiveEnv => EnvCombo.SelectedItem as EnvironmentModel;

    void UpdateTabsVisibility()
    {
        var any = OpenTabs.Count > 0;
        TabsHost.IsVisible = any;
        Placeholder.IsVisible = !any;
    }

    void RefreshEnvCombo()
    {
        var selected = ActiveEnv;
        var items = new List<object> { NoEnvironment };
        items.AddRange(Environments);
        items.Add(NewEnvironmentItem);
        items.Add(ManageEnvironmentsItem);

        _envComboGuard = true;
        EnvCombo.ItemsSource = items;
        EnvCombo.SelectedItem = selected != null && Environments.Contains(selected) ? selected : items[0];
        _lastRealEnvItem = EnvCombo.SelectedItem;
        _envComboGuard = false;
    }

    void SaveAll()
    {
        foreach (var c in Collections) Storage.SaveCollection(c);
        Storage.SaveEnvironments(Environments);
        Storage.SaveSettings(new AppSettings { ActiveEnvironmentId = ActiveEnv?.Id });
    }

    public void SaveAllForSync() => SaveAll();

    void UpdateStatusEnv()
    {
        StatusEnvText.Text = $"Ambiente: {ActiveEnv?.Name ?? "sin ambiente"}";
        UpdateVarsPanel();
    }

    // ----- Búsqueda de dueños -----

    (RequestCollection Collection, ObservableCollection<RequestItem> List)? FindOwner(RequestItem req)
    {
        foreach (var col in Collections)
        {
            if (col.Requests.Contains(req)) return (col, col.Requests);
            foreach (var folder in col.AllFolders)
                if (folder.Requests.Contains(req)) return (col, folder.Requests);
        }
        return null;
    }

    RequestCollection? FindOwnerCollection(Folder folder) => Collections.FirstOrDefault(c => c.AllFolders.Contains(folder));

    (RequestCollection Collection, ObservableCollection<Folder> List)? FindFolderParent(Folder folder)
    {
        foreach (var col in Collections)
        {
            if (col.Folders.Contains(folder)) return (col, col.Folders);
            foreach (var parent in col.AllFolders)
                if (parent.Folders.Contains(folder)) return (col, parent.Folders);
        }
        return null;
    }

    string GetRequestPath(RequestItem req)
    {
        foreach (var col in Collections)
        {
            if (col.Requests.Contains(req)) return col.Name;
            foreach (var folder in col.AllFolders)
                if (folder.Requests.Contains(req))
                    return $"{col.Name} › {FolderPath(col, folder)}";
        }
        return "";
    }

    static string FolderPath(RequestCollection col, Folder target)
    {
        var path = new List<string>();
        bool Walk(IEnumerable<Folder> folders, List<string> acc)
        {
            foreach (var f in folders)
            {
                acc.Add(f.Name);
                if (f == target || Walk(f.Folders, acc)) return true;
                acc.RemoveAt(acc.Count - 1);
            }
            return false;
        }
        Walk(col.Folders, path);
        return string.Join(" › ", path);
    }

    // ----- Tabs -----

    public void OpenTab(RequestItem req)
    {
        var tab = OpenTabs.OfType<RequestTab>().FirstOrDefault(t => t.Original == req);
        if (tab == null)
        {
            tab = new RequestTab(req, () => ActiveEnv, () => FindOwner(req)?.Collection,
                () => GetRequestPath(req), () => SaveRequestToOwnerOrTarget(req));
            OpenTabs.Add(tab);
        }
        RequestTabs.SelectedItem = tab;
    }

    public void OpenCollectionTab(RequestCollection col)
    {
        var tab = OpenTabs.OfType<CollectionTab>().FirstOrDefault(t => t.Collection == col);
        if (tab == null)
        {
            tab = new CollectionTab(col, () => Storage.SaveCollection(col));
            OpenTabs.Add(tab);
        }
        RequestTabs.SelectedItem = tab;
    }

    void OpenNewRequestTab()
    {
        var req = new RequestItem();
        var tab = new RequestTab(req, () => ActiveEnv, () => FindOwner(req)?.Collection,
            () => GetRequestPath(req), () => SaveRequestToOwnerOrTarget(req));
        tab.MarkAsNew();
        OpenTabs.Add(tab);
        RequestTabs.SelectedItem = tab;
    }

    void NewTab_Click(object? sender, RoutedEventArgs e) => OpenNewRequestTab();

    bool SaveRequestToOwnerOrTarget(RequestItem req)
    {
        var owner = FindOwner(req);
        if (owner != null) { Storage.SaveCollection(owner.Value.Collection); RefreshGitStatus(); return true; }

        if (Collections.Count == 0)
        {
            _ = Dialogs.Info(this, "Creá una colección primero para poder guardar la request.", "Guardar request");
            return false;
        }
        // diálogo síncrono vía ShowDialog no disponible acá sin async; usamos flujo diferido
        _ = SaveNewRequestAsync(req);
        return false;
    }

    async Task SaveNewRequestAsync(RequestItem req)
    {
        var dlg = new SaveTargetWindow(Collections);
        await dlg.ShowDialog(this);
        if (dlg.Selected is not { } target) return;
        if (target.Folder != null) target.Folder.Requests.Add(req);
        else target.Collection.Requests.Add(req);
        Storage.SaveCollection(target.Collection);
        var tab = OpenTabs.OfType<RequestTab>().FirstOrDefault(t => t.Original == req);
        tab?.Save();
        tab?.RefreshBreadcrumb();
        RefreshGitStatus();
    }

    async void CloseTab_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not { } tab) return;
        if (tab is RequestTab { IsDirty: true } dirty)
        {
            var r = await Dialogs.Confirm(this,
                $"\"{dirty.Request.Name}\" tiene cambios sin guardar. ¿Querés guardarlos?", "Cambios sin guardar");
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.Yes) dirty.Save();
        }
        RemoveTab(tab);
    }

    void RemoveTab(object tab)
    {
        (tab as RequestTab)?.Detach();
        OpenTabs.Remove(tab);
    }

    void CloseTabsOf(IEnumerable<RequestItem> requests)
    {
        var set = requests.ToHashSet();
        foreach (var tab in OpenTabs.OfType<RequestTab>().Where(t => set.Contains(t.Original)).ToList())
            RemoveTab(tab);
    }

    // ----- Runner -----

    public void OpenRunner(RequestItem? preselect = null)
    {
        SaveAll();
        var tab = OpenTabs.OfType<RunnerTab>().FirstOrDefault();
        if (tab == null)
        {
            tab = new RunnerTab(Collections, Environments, ActiveEnv);
            OpenTabs.Add(tab);
        }
        if (preselect != null && !tab.IsRunning) tab.PreselectRequest(preselect);
        RequestTabs.SelectedItem = tab;
    }

    void Runner_Click(object? sender, RoutedEventArgs e)
    {
        if (Collections.Count == 0)
        {
            _ = Dialogs.Info(this, "Creá al menos una colección con requests para usar el runner.", "Runner");
            return;
        }
        OpenRunner();
    }

    // ----- Toolbar / sidebar -----

    async void NewCollection_Click(object? sender, RoutedEventArgs e)
    {
        var name = await Dialogs.Prompt(this, "Nueva colección", "Nombre de la colección:", "Mi colección");
        if (string.IsNullOrWhiteSpace(name)) return;
        var col = new RequestCollection { Name = name.Trim() };
        Collections.Add(col);
        Storage.SaveCollection(col);
        RefreshGitStatus();
    }

    async void ImportOpenApi_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Importar OpenAPI",
            AllowMultiple = false
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        var dlg = new ImportWindow(path);
        await dlg.ShowDialog(this);
        if (dlg.ImportedCollection is not { } col) return;

        Collections.Add(col);
        Storage.SaveCollection(col);
        if (!string.IsNullOrWhiteSpace(dlg.BaseUrl))
        {
            var env = new EnvironmentModel { Name = col.Name };
            env.Variables.Add(new KeyValueItem { Key = "baseUrl", Value = dlg.BaseUrl });
            Environments.Add(env);
            Storage.SaveEnvironments(Environments);
            RefreshEnvCombo();
            EnvCombo.SelectedItem = env;
        }
        RefreshGitStatus();
    }

    async void OpenEnvironmentsManager()
    {
        await new EnvironmentsWindow(Environments).ShowDialog(this);
        Storage.SaveEnvironments(Environments);
        RefreshEnvCombo();
    }

    async void CreateNewEnvironment()
    {
        var name = await Dialogs.Prompt(this, "Nuevo ambiente", "Nombre del ambiente:", "Desarrollo");
        if (string.IsNullOrWhiteSpace(name)) return;
        var env = new EnvironmentModel { Name = name.Trim() };
        Environments.Add(env);
        Storage.SaveEnvironments(Environments);
        RefreshEnvCombo();
        EnvCombo.SelectedItem = env;
        if (VarsToggle.IsChecked != true) VarsToggle.IsChecked = true;
    }

    void EnvCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _envComboGuard) return;

        if (EnvCombo.SelectedItem is string s && s is NewEnvironmentItem or ManageEnvironmentsItem)
        {
            _envComboGuard = true;
            EnvCombo.SelectedItem = _lastRealEnvItem;
            _envComboGuard = false;
            if (s == NewEnvironmentItem) CreateNewEnvironment();
            else OpenEnvironmentsManager();
            return;
        }

        _lastRealEnvItem = EnvCombo.SelectedItem;
        Storage.SaveSettings(new AppSettings { ActiveEnvironmentId = ActiveEnv?.Id });
        UpdateStatusEnv();
    }

    void SidebarMenu_Click(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var import = new MenuItem { Header = "Importar OpenAPI" };
        import.Click += ImportOpenApi_Click;
        var newCol = new MenuItem { Header = "Nueva colección" };
        newCol.Click += NewCollection_Click;
        var ws = new MenuItem { Header = "Workspace y Git…" };
        ws.Click += Workspace_Click;
        menu.Items.Add(import);
        menu.Items.Add(newCol);
        menu.Items.Add(new Separator());
        menu.Items.Add(ws);
        menu.Open(sender as Control);
    }

    // ----- Árbol -----

    void Tree_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // resolver el nodo tocado desde el árbol visual (más confiable que SelectedItem)
        var node = e.Source as global::Avalonia.StyledElement;
        object? data = null;
        while (node != null)
        {
            if (node.DataContext is RequestItem or RequestCollection or Folder) { data = node.DataContext; break; }
            node = node.Parent;
        }
        data ??= Tree.SelectedItem;

        switch (data)
        {
            case RequestItem req: OpenTab(req); break;
            case RequestCollection col: OpenCollectionTab(col); break;
        }
    }

    // ----- Filtro -----

    void Filter_Changed(object? sender, TextChangedEventArgs e) => ApplyFilter(FilterBox.Text?.Trim() ?? "");

    public void ApplyFilter(string filter)
    {
        var f = filter.ToLowerInvariant();
        foreach (var col in Collections)
        {
            var colMatch = MatchFilterInto(col, f);
            col.IsVisibleInTree = f.Length == 0 || colMatch;
            if (f.Length > 0 && colMatch) col.IsExpandedInTree = true;
        }
    }

    static bool MatchFilterInto(RequestCollection col, string f)
    {
        var any = false;
        foreach (var folder in col.Folders) any |= MatchFolder(folder, f);
        foreach (var req in col.Requests) { var m = MatchReq(req, f); req.IsVisibleInTree = f.Length == 0 || m; any |= m; }
        return any || f.Length == 0;
    }

    static bool MatchFolder(Folder folder, string f)
    {
        var any = false;
        foreach (var sub in folder.Folders) any |= MatchFolder(sub, f);
        foreach (var req in folder.Requests) { var m = MatchReq(req, f); req.IsVisibleInTree = f.Length == 0 || m; any |= m; }
        folder.IsVisibleInTree = f.Length == 0 || any;
        if (f.Length > 0 && any) folder.IsExpandedInTree = true;
        return any;
    }

    static bool MatchReq(RequestItem req, string f) =>
        f.Length == 0 ||
        req.Name.ToLowerInvariant().Contains(f) ||
        req.Url.ToLowerInvariant().Contains(f) ||
        req.Method.ToLowerInvariant().Contains(f);

    // ----- Paneles y estado -----

    void SidebarToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (SidebarBorder == null) return;
        SidebarBorder.IsVisible = SidebarToggle.IsChecked == true;
    }

    void VarsToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (VarsPanel == null) return;
        var show = VarsToggle.IsChecked == true;
        VarsPanel.IsVisible = show;
        VarsSplitter.IsVisible = show;
        if (show) UpdateVarsPanel();
    }

    void UpdateVarsPanel()
    {
        if (VarsGrid == null) return;
        var env = ActiveEnv;
        VarsGrid.Bind(env?.Variables, "VARIABLE", "VALOR");
        VarsEnvName.Text = env?.Name ?? "";
        VarsGrid.IsEnabled = env != null;
        SaveVarsBtn.IsEnabled = env != null;
    }

    void SaveVars_Click(object? sender, RoutedEventArgs e) => Storage.SaveEnvironments(Environments);

    void LogsToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (LogsPanel == null) return;
        var show = LogsToggle.IsChecked == true;
        LogsPanel.IsVisible = show;
        LogsSplitter.IsVisible = show;
    }

    void ClearLogs_Click(object? sender, RoutedEventArgs e) => ExecutionLog.Entries.Clear();

    // ----- Workspace / git -----

    /// <summary>Activa un workspace (null = Personal). Cada workspace es independiente: se cargan
    /// SUS colecciones (o queda vacío). Las colecciones de los otros workspaces no se tocan.</summary>
    public async Task<bool> SwitchWorkspace(string? path)
    {
        var dirty = OpenTabs.OfType<RequestTab>().Where(t => t.IsDirty).ToList();
        if (dirty.Count > 0)
        {
            var r = await Dialogs.Confirm(this,
                $"Hay {dirty.Count} pestaña(s) con cambios sin guardar. ¿Guardarlos antes de cambiar de workspace?",
                "Cambios sin guardar");
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) foreach (var t in dirty) t.Save();
        }
        SaveAll();

        Storage.SetWorkspacePath(path);
        foreach (var tab in OpenTabs.ToList()) RemoveTab(tab);

        Collections.Clear();
        foreach (var col in Storage.LoadCollections()) Collections.Add(col);
        ApplyFilter(FilterBox.Text?.Trim() ?? "");
        UpdateStatusEnv();
        RefreshGitStatus();
        return true;
    }

    public void RefreshGitStatus()
    {
        var root = Storage.WorkspaceRoot;
        Task.Run(() =>
        {
            if (!GitService.IsAvailable() || !GitService.IsRepo(root)) return (GitStatusInfo?)null;
            return GitService.Status(root);
        }).ContinueWith(t =>
        {
            var s = t.IsCompletedSuccessfully ? t.Result : null;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var ws = Storage.ActiveWorkspaceName;
                if (s == null)
                {
                    GitStatusBtn.Content = $"◆ {ws}";
                }
                else
                {
                    var text = $"◆ {ws}  ⎇ {s.Branch}";
                    text += s.Pending > 0 ? $" · {s.Pending} cambio(s)" : " ✓";
                    if (s.Ahead > 0) text += $" ↑{s.Ahead}";
                    if (s.Behind > 0) text += $" ↓{s.Behind}";
                    GitStatusBtn.Content = text;
                }
            });
        });
    }

    async void Workspace_Click(object? sender, RoutedEventArgs e)
    {
        await new WorkspaceWindow(this).ShowDialog(this);
        RefreshGitStatus();
    }

    async void OnClosing(object? sender, global::System.ComponentModel.CancelEventArgs e)
    {
        var dirty = OpenTabs.OfType<RequestTab>().Where(t => t.IsDirty).ToList();
        if (dirty.Count > 0 && !_closingConfirmed)
        {
            e.Cancel = true;
            var r = await Dialogs.Confirm(this,
                $"Hay {dirty.Count} pestaña(s) con cambios sin guardar. ¿Guardarlos antes de salir?", "Cambios sin guardar");
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.Yes) foreach (var t in dirty) t.Save();
            SaveAll();
            _closingConfirmed = true;
            Close();
            return;
        }
        SaveAll();
    }

    bool _closingConfirmed;

    // ----- Drag & drop del árbol -----

    const string DragFormat = "er-tree-item";
    global::Avalonia.Point _dragStart;
    object? _dragCandidate;

    static object? NodeData(object? source)
    {
        var node = source as global::Avalonia.StyledElement;
        while (node != null)
        {
            if (node.DataContext is RequestItem or Folder or RequestCollection) return node.DataContext;
            node = node.Parent;
        }
        return null;
    }

    void Tree_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragCandidate = null;
        var data = NodeData(e.Source);
        if (data is RequestItem or Folder)  // colecciones no se arrastran
        {
            _dragStart = e.GetPosition(Tree);
            _dragCandidate = data;
        }
    }

    async void Tree_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate == null || !e.GetCurrentPoint(Tree).Properties.IsLeftButtonPressed) return;
        var pos = e.GetPosition(Tree);
        if (Math.Abs(pos.X - _dragStart.X) < 6 && Math.Abs(pos.Y - _dragStart.Y) < 6) return;

        var dragged = _dragCandidate;
        _dragCandidate = null;
        var data = new DataObject();
        data.Set(DragFormat, dragged!);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    void Tree_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = ResolveDrop(e, out _, out _, out _) != DropKind.None
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    void Tree_Drop(object? sender, DragEventArgs e)
    {
        var kind = ResolveDrop(e, out var dragged, out var target, out var after);
        e.Handled = true;
        switch (kind)
        {
            case DropKind.ReorderRequest when dragged is RequestItem req && target is RequestItem tr:
                MoveRequestNextTo(req, tr, after); break;
            case DropKind.MoveRequestInto when dragged is RequestItem req:
                MoveRequestInto(req, target!); break;
            case DropKind.ReorderFolder when dragged is Folder f && target is Folder tf:
                ReorderFolder(f, tf, after); break;
        }
    }

    enum DropKind { None, ReorderRequest, MoveRequestInto, ReorderFolder }

    DropKind ResolveDrop(DragEventArgs e, out object? dragged, out object? target, out bool after)
    {
        dragged = e.Data.Contains(DragFormat) ? e.Data.Get(DragFormat) : null;
        target = NodeData(e.Source);
        after = false;
        if (dragged == null || target == null || ReferenceEquals(dragged, target)) return DropKind.None;

        // mitad superior de la fila = antes; inferior = después
        var node = e.Source as global::Avalonia.StyledElement;
        while (node != null && node.DataContext != target) node = node.Parent;
        if (node is global::Avalonia.Visual visual)
        {
            var y = e.GetPosition(visual).Y;
            after = y > visual.Bounds.Height / 2;
        }

        switch (dragged)
        {
            case RequestItem:
                return target switch
                {
                    RequestItem => DropKind.ReorderRequest,
                    Folder or RequestCollection => DropKind.MoveRequestInto,
                    _ => DropKind.None
                };
            case Folder df when target is Folder tf:
                var sp = FindFolderParent(df);
                var tp = FindFolderParent(tf);
                return sp != null && tp != null && ReferenceEquals(sp.Value.List, tp.Value.List)
                    ? DropKind.ReorderFolder : DropKind.None;
            default:
                return DropKind.None;
        }
    }

    void MoveRequestNextTo(RequestItem req, RequestItem targetReq, bool after)
    {
        var source = FindOwner(req);
        var dest = FindOwner(targetReq);
        if (source == null || dest == null) return;
        source.Value.List.Remove(req);
        var index = dest.Value.List.IndexOf(targetReq) + (after ? 1 : 0);
        dest.Value.List.Insert(Math.Clamp(index, 0, dest.Value.List.Count), req);
        Storage.SaveCollection(source.Value.Collection);
        if (dest.Value.Collection != source.Value.Collection) Storage.SaveCollection(dest.Value.Collection);
        OpenTabs.OfType<RequestTab>().FirstOrDefault(t => t.Original == req)?.RefreshBreadcrumb();
        RefreshGitStatus();
    }

    void MoveRequestInto(RequestItem req, object container)
    {
        var source = FindOwner(req);
        if (source == null) return;
        var destList = container switch
        {
            Folder folder => folder.Requests,
            RequestCollection col => col.Requests,
            _ => null
        };
        if (destList == null || ReferenceEquals(destList, source.Value.List)) return;
        var destCollection = container switch
        {
            Folder folder => FindOwnerCollection(folder),
            RequestCollection col => col,
            _ => null
        };
        if (destCollection == null) return;
        source.Value.List.Remove(req);
        destList.Add(req);
        if (container is Folder f) f.IsExpandedInTree = true;
        Storage.SaveCollection(source.Value.Collection);
        if (destCollection != source.Value.Collection) Storage.SaveCollection(destCollection);
        OpenTabs.OfType<RequestTab>().FirstOrDefault(t => t.Original == req)?.RefreshBreadcrumb();
        RefreshGitStatus();
    }

    void ReorderFolder(Folder folder, Folder targetFolder, bool after)
    {
        var parent = FindFolderParent(folder);
        if (parent == null) return;
        parent.Value.List.Remove(folder);
        var index = parent.Value.List.IndexOf(targetFolder) + (after ? 1 : 0);
        parent.Value.List.Insert(Math.Clamp(index, 0, parent.Value.List.Count), folder);
        Storage.SaveCollection(parent.Value.Collection);
        RefreshGitStatus();
    }
}
