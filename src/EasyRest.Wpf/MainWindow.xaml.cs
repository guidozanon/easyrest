using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest;

public partial class MainWindow : Window
{
    public ObservableCollection<RequestCollection> Collections { get; } = new();
    public ObservableCollection<EnvironmentModel> Environments { get; } = new();
    public ObservableCollection<object> OpenTabs { get; } = new();

    const string NoEnvironment = "(Sin ambiente)";

    public MainWindow()
    {
        InitializeComponent();

        foreach (var c in Storage.LoadCollections()) Collections.Add(c);
        foreach (var e in Storage.LoadEnvironments()) Environments.Add(e);
        Tree.ItemsSource = Collections;
        RequestTabs.ItemsSource = OpenTabs;
        OpenTabs.CollectionChanged += (_, _) => UpdateTabsVisibility();
        RequestTabs.SelectionChanged += RequestTabs_SelectionChanged;
        RefreshEnvCombo();

        var settings = Storage.LoadSettings();
        var activeEnv = Environments.FirstOrDefault(e => e.Id == settings.ActiveEnvironmentId);
        if (activeEnv != null) EnvCombo.SelectedItem = activeEnv;

        LogsItems.ItemsSource = ExecutionLog.Entries;
        ExecutionLog.Entries.CollectionChanged += (_, _) =>
        {
            UpdateLogsToggle();
            if (LogsPanel.IsVisible) LogsScroll.ScrollToEnd();
        };
        UpdateLogsToggle();
        UpdateStatusEnv();

        Closing += MainWindow_Closing;
        Loaded += (_, _) => RefreshGitStatus();

        // refresco periódico del estado de git (los guardados también lo disparan)
        var gitTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        gitTimer.Tick += (_, _) => RefreshGitStatus();
        gitTimer.Start();

        // Ctrl+S guarda la pestaña activa
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (RequestTabs.SelectedItem)
                {
                    case RequestTab rt: rt.Save(); break;
                    case CollectionTab ct: ct.Save(); break;
                }
                e.Handled = true;
            }
        };
    }

    void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var dirtyTabs = OpenTabs.OfType<RequestTab>().Where(t => t.IsDirty).ToList();
        if (dirtyTabs.Count > 0)
        {
            var result = MessageBox.Show(this,
                $"Hay {dirtyTabs.Count} pestaña(s) con cambios sin guardar. ¿Querés guardarlos antes de salir?",
                "Cambios sin guardar", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (result == MessageBoxResult.Yes)
                foreach (var tab in dirtyTabs) tab.Save();
        }
        SaveAll();
    }

    GridLength _sidebarWidth = new(310);
    GridLength _varsWidth = new(300);

    void UpdateLogsToggle() =>
        LogsToggle.Content = ExecutionLog.Entries.Count > 0 ? $"☰ Logs ({ExecutionLog.Entries.Count})" : "☰ Logs";

    void UpdateStatusEnv()
    {
        StatusEnvText.Text = $"Ambiente: {ActiveEnv?.Name ?? "sin ambiente"}";
        UpdateVarsPanel();
    }

    void SidebarToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (SidebarCol == null) return; // Checked inicial dispara antes de que exista la grilla
        var show = SidebarToggle.IsChecked == true;
        if (show)
        {
            SidebarCol.Width = _sidebarWidth;
            SidebarSplitterCol.Width = new GridLength(4);
        }
        else
        {
            if (SidebarCol.Width.Value > 0) _sidebarWidth = SidebarCol.Width;
            SidebarCol.Width = new GridLength(0);
            SidebarSplitterCol.Width = new GridLength(0);
        }
    }

    void VarsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (VarsCol == null) return;
        var show = VarsToggle.IsChecked == true;
        if (show)
        {
            VarsCol.Width = _varsWidth;
            VarsSplitterCol.Width = new GridLength(4);
            UpdateVarsPanel();
        }
        else
        {
            if (VarsCol.Width.Value > 0) _varsWidth = VarsCol.Width;
            VarsCol.Width = new GridLength(0);
            VarsSplitterCol.Width = new GridLength(0);
        }
        VarsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        VarsSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateVarsPanel()
    {
        if (VarsGrid == null) return;
        var env = ActiveEnv;
        VarsGrid.ItemsSource = env?.Variables;
        VarsEnvName.Text = env?.Name ?? "";
        VarsGrid.Visibility = env != null ? Visibility.Visible : Visibility.Collapsed;
        SaveVarsBtn.IsEnabled = env != null;
        NoEnvText.Visibility = env == null ? Visibility.Visible : Visibility.Collapsed;
    }

    void SaveVars_Click(object sender, RoutedEventArgs e) => Storage.SaveEnvironments(Environments);

    void LogsToggle_Changed(object sender, RoutedEventArgs e)
    {
        var open = LogsToggle.IsChecked == true;
        LogsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        LogsSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        LogsRow.Height = open ? new GridLength(280) : GridLength.Auto;
        if (open) LogsScroll.ScrollToEnd();
    }

    void ClearLogs_Click(object sender, RoutedEventArgs e) => ExecutionLog.Entries.Clear();

    public EnvironmentModel? ActiveEnv => EnvCombo.SelectedItem as EnvironmentModel;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // WPF no rutea la rueda lateral del mouse (WM_MOUSEHWHEEL): lo manejamos a mano
        if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source)
            source.AddHook(WndProc);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEHWHEEL = 0x020E;
        if (msg == WM_MOUSEHWHEEL)
        {
            var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            var scroll = FindScrollableAncestor(Mouse.DirectlyOver as DependencyObject);
            if (scroll != null)
            {
                scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + delta);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    static ScrollViewer? FindScrollableAncestor(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is ScrollViewer { ScrollableWidth: > 0 } sv) return sv;
            d = App.GetParent(d);
        }
        return null;
    }

    void UpdateTabsVisibility()
    {
        var any = OpenTabs.Count > 0;
        RequestTabs.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        Placeholder.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    void RequestTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged burbujea desde los TabControls internos del editor: ignorarlos
        if (e.OriginalSource != RequestTabs) return;
        var selected = RequestTabs.SelectedItem;
        if (selected == null) return;
        // Diferido: el container puede no estar generado todavía si el tab se acaba de agregar
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (RequestTabs.ItemContainerGenerator.ContainerFromItem(selected) is FrameworkElement item)
                item.BringIntoView();
        });
    }

    void RefreshEnvCombo()
    {
        var selected = ActiveEnv;
        var items = new List<object> { NoEnvironment };
        items.AddRange(Environments);
        EnvCombo.ItemsSource = items;
        EnvCombo.SelectedItem = selected != null && Environments.Contains(selected) ? selected : items[0];
    }

    void SaveAll()
    {
        foreach (var c in Collections) Storage.SaveCollection(c);
        Storage.SaveEnvironments(Environments);
        Storage.SaveSettings(new AppSettings { ActiveEnvironmentId = ActiveEnv?.Id });
    }

    /// <summary>Persiste todo antes de un sync de git (usado por la ventana de workspace).</summary>
    public void SaveAllForSync() => SaveAll();

    // ----- Workspace y git -----

    /// <summary>Cambia la carpeta del workspace. Si la nueva carpeta está vacía exporta las
    /// colecciones actuales; si ya tiene colecciones, las carga. Devuelve false si se cancela.</summary>
    public bool SwitchWorkspace(string? path)
    {
        var dirtyTabs = OpenTabs.OfType<RequestTab>().Where(t => t.IsDirty).ToList();
        if (dirtyTabs.Count > 0)
        {
            var result = MessageBox.Show(this,
                $"Hay {dirtyTabs.Count} pestaña(s) con cambios sin guardar. ¿Querés guardarlos antes de cambiar de workspace?",
                "Cambios sin guardar", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.Yes)
                foreach (var tab in dirtyTabs) tab.Save();
        }
        SaveAll(); // persiste bajo el workspace actual antes de cambiar

        var exporting = false;
        if (path != null)
        {
            var dir = System.IO.Path.Combine(path, "collections");
            exporting = !System.IO.Directory.Exists(dir) ||
                        System.IO.Directory.GetFiles(dir, "*.json").Length == 0;
        }

        Storage.SetWorkspacePath(path);

        // las pestañas apuntan a objetos del árbol viejo: cerrarlas
        foreach (var tab in OpenTabs.ToList()) RemoveTab(tab);

        if (exporting)
        {
            foreach (var col in Collections) Storage.SaveCollection(col);
        }
        else
        {
            Collections.Clear();
            foreach (var col in Storage.LoadCollections()) Collections.Add(col);
        }
        ApplyFilter(FilterBox.Text.Trim());
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
            var status = t.IsCompletedSuccessfully ? t.Result : null;
            Dispatcher.Invoke(() =>
            {
                if (status == null)
                {
                    GitStatusBtn.Content = "⎇ workspace";
                    GitStatusBtn.ToolTip = "Configurar el workspace y git";
                }
                else
                {
                    var text = status.Pending > 0
                        ? $"⎇ {status.Branch} · {status.Pending} cambio(s)"
                        : $"⎇ {status.Branch} ✓";
                    if (status.Ahead > 0) text += $" ↑{status.Ahead}";
                    if (status.Behind > 0) text += $" ↓{status.Behind}";
                    GitStatusBtn.Content = text;
                    GitStatusBtn.ToolTip = $"Workspace: {root}\nremote: {status.Remote ?? "(sin configurar)"}";
                }
            });
        });
    }

    void GitStatus_Click(object sender, RoutedEventArgs e) => OpenWorkspaceWindow();

    void Workspace_Click(object sender, RoutedEventArgs e) => OpenWorkspaceWindow();

    void OpenWorkspaceWindow()
    {
        new WorkspaceWindow(this) { Owner = this }.ShowDialog();
        RefreshGitStatus();
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

    RequestCollection? FindOwnerCollection(Folder folder) =>
        Collections.FirstOrDefault(c => c.AllFolders.Contains(folder));

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

    public void SaveOwnerOf(RequestItem req)
    {
        var owner = FindOwner(req);
        if (owner != null) Storage.SaveCollection(owner.Value.Collection);
    }

    /// <summary>Ruta "Colección › Carpeta › …" de la request (sin el nombre de la request).</summary>
    public string GetRequestPath(RequestItem req)
    {
        foreach (var col in Collections)
        {
            if (col.Requests.Contains(req)) return col.Name;
            var path = FindPathInFolders(col.Folders, req, col.Name);
            if (path != null) return path;
        }
        return "";
    }

    static string? FindPathInFolders(IEnumerable<Folder> folders, RequestItem req, string prefix)
    {
        foreach (var folder in folders)
        {
            var path = $"{prefix} › {folder.Name}";
            if (folder.Requests.Contains(req)) return path;
            var deeper = FindPathInFolders(folder.Folders, req, path);
            if (deeper != null) return deeper;
        }
        return null;
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

    /// <summary>Crea una request nueva en una pestaña, sin pedir nombre ni asignarla a una colección.
    /// Al guardarla se pregunta el destino.</summary>
    public void OpenNewRequestTab()
    {
        var req = new RequestItem();
        var tab = new RequestTab(req, () => ActiveEnv, () => FindOwner(req)?.Collection,
            () => GetRequestPath(req), () => SaveRequestToOwnerOrTarget(req));
        tab.MarkAsNew();
        OpenTabs.Add(tab);
        RequestTabs.SelectedItem = tab;
    }

    void NewTab_Click(object sender, RoutedEventArgs e) => OpenNewRequestTab();

    /// <summary>Guarda la colección dueña de la request; si la request no pertenece a ninguna
    /// (pestaña nueva), pregunta el destino. Devuelve false si el usuario cancela.</summary>
    bool SaveRequestToOwnerOrTarget(RequestItem req)
    {
        var owner = FindOwner(req);
        if (owner != null)
        {
            Storage.SaveCollection(owner.Value.Collection);
            return true;
        }

        if (Collections.Count == 0)
        {
            MessageBox.Show(this, "Creá una colección primero para poder guardar la request.", "Guardar request");
            return false;
        }

        var dlg = new SaveTargetWindow(Collections) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Selected is not { } target) return false;

        if (target.Folder != null) target.Folder.Requests.Add(req);
        else target.Collection.Requests.Add(req);
        Storage.SaveCollection(target.Collection);
        OpenTabs.OfType<RequestTab>().FirstOrDefault(t => t.Original == req)?.RefreshBreadcrumb();
        return true;
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

    void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not { } tab) return;

        if (tab is RequestTab { IsDirty: true } dirty)
        {
            var result = MessageBox.Show(this,
                $"\"{dirty.Request.Name}\" tiene cambios sin guardar. ¿Querés guardarlos?",
                "Cambios sin guardar", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel) return;
            if (result == MessageBoxResult.Yes) dirty.Save();
        }
        RemoveTab(tab);
    }

    void RemoveTab(object tab)
    {
        (tab as RequestTab)?.Detach();
        (tab as RunnerTab)?.Stop();
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

    public void OpenRunnerFor(RequestItem req) => OpenRunner(req);

    void Runner_Click(object sender, RoutedEventArgs e)
    {
        if (Collections.Count == 0)
        {
            MessageBox.Show(this, "Creá al menos una colección con requests para usar el runner.", "Runner");
            return;
        }
        OpenRunner();
    }

    // ----- Toolbar -----

    void NewCollection_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptWindow.Show(this, "Nueva colección", "Nombre de la colección:", "Mi colección");
        if (string.IsNullOrWhiteSpace(name)) return;
        var col = new RequestCollection { Name = name.Trim() };
        Collections.Add(col);
        Storage.SaveCollection(col);
    }

    void ImportOpenApi_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importar OpenAPI",
            Filter = "OpenAPI (*.json;*.yaml;*.yml)|*.json;*.yaml;*.yml|Todos los archivos|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        var importDlg = new ImportWindow(dlg.FileName) { Owner = this };
        importDlg.ShowDialog();

        if (importDlg.ImportedCollection is not { } col) return;
        Collections.Add(col);
        Storage.SaveCollection(col);

        if (!string.IsNullOrWhiteSpace(importDlg.BaseUrl))
        {
            var env = new EnvironmentModel { Name = col.Name };
            env.Variables.Add(new KeyValueItem { Key = "baseUrl", Value = importDlg.BaseUrl });
            Environments.Add(env);
            Storage.SaveEnvironments(Environments);
            RefreshEnvCombo();
            EnvCombo.SelectedItem = env;
        }
    }

    void SidebarMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    // ----- Filtro del árbol -----

    void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var filter = FilterBox.Text.Trim();
        FilterHint.Visibility = FilterBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter(filter);
    }

    void ApplyFilter(string filter)
    {
        var filtering = filter.Length > 0;
        foreach (var col in Collections)
        {
            var any = false;
            foreach (var folder in col.Folders) any |= FilterFolder(folder, filter, filtering);
            foreach (var req in col.Requests)
            {
                var match = !filtering || Matches(req, filter);
                req.IsVisibleInTree = match;
                any |= match;
            }
            col.IsVisibleInTree = !filtering || any;
            if (filtering && any) col.IsExpandedInTree = true;
        }
    }

    static bool FilterFolder(Folder folder, string filter, bool filtering)
    {
        var any = false;
        foreach (var sub in folder.Folders) any |= FilterFolder(sub, filter, filtering);
        foreach (var req in folder.Requests)
        {
            var match = !filtering || Matches(req, filter);
            req.IsVisibleInTree = match;
            any |= match;
        }
        folder.IsVisibleInTree = !filtering || any;
        if (filtering && any) folder.IsExpandedInTree = true;
        return any;
    }

    static bool Matches(RequestItem req, string filter) =>
        req.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        req.Url.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        req.Method.Contains(filter, StringComparison.OrdinalIgnoreCase);

    void Environments_Click(object sender, RoutedEventArgs e)
    {
        new EnvironmentsWindow(Environments) { Owner = this }.ShowDialog();
        Storage.SaveEnvironments(Environments);
        RefreshEnvCombo();
    }

    void EnvCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        Storage.SaveSettings(new AppSettings { ActiveEnvironmentId = ActiveEnv?.Id });
        UpdateStatusEnv();
    }

    // ----- Árbol -----

    void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        switch (item?.DataContext)
        {
            case RequestItem req:
                OpenTab(req);
                e.Handled = true;
                break;
            case RequestCollection col:
                OpenCollectionTab(col);
                e.Handled = true;
                break;
        }
    }

    void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item != null)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    // ----- Drag & drop en el árbol -----

    const string DragFormat = "EasyRestTreeItem";
    Point _dragStart;
    object? _dragCandidate;

    void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        // no arrancar drags desde el botón ⋯
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is RequestItem or Folder)
        {
            _dragStart = e.GetPosition(Tree);
            _dragCandidate = item.DataContext;
        }
    }

    void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(Tree);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var dragged = _dragCandidate;
        _dragCandidate = null;
        DragDrop.DoDragDrop(Tree, new DataObject(DragFormat, dragged), DragDropEffects.Move);
        RemoveDragAdorner(); // cubre drop fuera del árbol y cancelación con Esc
    }

    void Tree_DragOver(object sender, DragEventArgs e)
    {
        var action = GetDropAction(e, out var dragged, out _, out var insertAfter, out var targetItem);
        e.Effects = action != DropAction.None ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        UpdateDragAdorner(e, action, dragged, insertAfter, targetItem);
    }

    void Tree_DragLeave(object sender, DragEventArgs e)
    {
        // los DragLeave burbujean también al pasar entre filas: remover diferido si no hubo otro DragOver
        var stamp = _lastDragOver;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (_lastDragOver == stamp) RemoveDragAdorner();
        });
    }

    void Tree_Drop(object sender, DragEventArgs e)
    {
        RemoveDragAdorner();
        var action = GetDropAction(e, out var dragged, out var target, out var insertAfter, out _);
        if (action == DropAction.None) return;
        e.Handled = true;

        switch (action)
        {
            case DropAction.ReorderRequest when dragged is RequestItem req && target is RequestItem targetReq:
                MoveRequestNextTo(req, targetReq, insertAfter);
                break;
            case DropAction.MoveRequestInto when dragged is RequestItem req:
                MoveRequestInto(req, target!);
                break;
            case DropAction.ReorderFolder when dragged is Folder folder && target is Folder targetFolder:
                ReorderFolder(folder, targetFolder, insertAfter);
                break;
        }
    }

    enum DropAction { None, ReorderRequest, MoveRequestInto, ReorderFolder }

    // ----- Feedback visual del drag -----

    TreeDragAdorner? _dragAdorner;
    long _lastDragOver;

    TreeDragAdorner EnsureDragAdorner()
    {
        if (_dragAdorner == null)
        {
            _dragAdorner = new TreeDragAdorner(Tree);
            AdornerLayer.GetAdornerLayer(Tree)?.Add(_dragAdorner);
        }
        return _dragAdorner;
    }

    void RemoveDragAdorner()
    {
        if (_dragAdorner == null) return;
        AdornerLayer.GetAdornerLayer(Tree)?.Remove(_dragAdorner);
        _dragAdorner = null;
    }

    void UpdateDragAdorner(DragEventArgs e, DropAction action, object? dragged, bool insertAfter,
        TreeViewItem? targetItem)
    {
        _lastDragOver++;
        var adorner = EnsureDragAdorner();

        adorner.Label = dragged switch
        {
            RequestItem req => $"{req.Method}  {req.Name}",
            Folder folder => $"📁 {folder.Name}",
            _ => ""
        };
        adorner.CursorPosition = e.GetPosition(Tree);
        adorner.InsertionLine = null;
        adorner.Highlight = null;

        if (targetItem != null && action != DropAction.None)
        {
            var row = RowBounds(targetItem);
            if (action == DropAction.MoveRequestInto)
            {
                adorner.Highlight = row;
            }
            else
            {
                var y = insertAfter ? row.Bottom : row.Top;
                adorner.InsertionLine = new Rect(row.X + 4, y, Math.Max(40, Tree.ActualWidth - row.X - 20), 0);
            }
        }
        adorner.InvalidateVisual();
    }

    /// <summary>Rectángulo (relativo al árbol) de la fila de encabezado del item.</summary>
    Rect RowBounds(TreeViewItem item)
    {
        if (item.Template?.FindName("Bd", item) is Border bd && bd.IsVisible)
        {
            var origin = bd.TransformToAncestor(Tree).Transform(new Point(0, 0));
            return new Rect(origin, new Size(bd.ActualWidth, bd.ActualHeight));
        }
        var fallback = item.TransformToAncestor(Tree).Transform(new Point(0, 0));
        return new Rect(fallback, new Size(item.ActualWidth, 24));
    }

    DropAction GetDropAction(DragEventArgs e, out object? dragged, out object? target, out bool insertAfter,
        out TreeViewItem? targetItem)
    {
        dragged = e.Data.GetDataPresent(DragFormat) ? e.Data.GetData(DragFormat) : null;
        target = null;
        insertAfter = false;
        targetItem = null;
        if (dragged == null) return DropAction.None;

        targetItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (targetItem == null) return DropAction.None;
        target = targetItem.DataContext;
        if (ReferenceEquals(target, dragged)) return DropAction.None;

        // mitad superior de la fila = insertar antes; mitad inferior = después
        insertAfter = e.GetPosition(targetItem).Y > RowBounds(targetItem).Height / 2;

        switch (dragged)
        {
            case RequestItem:
                return target switch
                {
                    RequestItem => DropAction.ReorderRequest,
                    Folder or RequestCollection => DropAction.MoveRequestInto,
                    _ => DropAction.None
                };
            case Folder draggedFolder when target is Folder targetFolder:
                // solo reordenar dentro del mismo nivel (misma colección/carpeta padre)
                var sourceParent = FindFolderParent(draggedFolder);
                var targetParent = FindFolderParent(targetFolder);
                return sourceParent != null && targetParent != null &&
                       ReferenceEquals(sourceParent.Value.List, targetParent.Value.List)
                    ? DropAction.ReorderFolder
                    : DropAction.None;
            default:
                return DropAction.None;
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
    }

    void ReorderFolder(Folder folder, Folder targetFolder, bool after)
    {
        var parent = FindFolderParent(folder);
        if (parent == null) return;

        parent.Value.List.Remove(folder);
        var index = parent.Value.List.IndexOf(targetFolder) + (after ? 1 : 0);
        parent.Value.List.Insert(Math.Clamp(index, 0, parent.Value.List.Count), folder);
        Storage.SaveCollection(parent.Value.Collection);
    }

    static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    void TreeMenu_Opened(object sender, RoutedEventArgs e)
    {
        var sel = Tree.SelectedItem;
        MenuOpenTab.Visibility = sel is RequestItem ? Visibility.Visible : Visibility.Collapsed;
        MenuRunRequest.Visibility = sel is RequestItem ? Visibility.Visible : Visibility.Collapsed;
        MenuDuplicate.Visibility = sel is RequestItem ? Visibility.Visible : Visibility.Collapsed;
        MenuNewRequest.Visibility = sel is RequestCollection or Folder ? Visibility.Visible : Visibility.Collapsed;
        MenuNewFolder.Visibility = sel is RequestCollection or Folder ? Visibility.Visible : Visibility.Collapsed;
        MenuExpandAll.Visibility = sel is RequestCollection or Folder ? Visibility.Visible : Visibility.Collapsed;
        MenuCollapseAll.Visibility = sel is RequestCollection or Folder ? Visibility.Visible : Visibility.Collapsed;
        MenuRename.IsEnabled = sel != null;
        MenuDelete.IsEnabled = sel != null;
    }

    /// <summary>Botón ⋯ de la fila: selecciona el item y abre el mismo menú contextual del árbol.</summary>
    void RowMenu_Click(object sender, RoutedEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(sender as DependencyObject);
        if (item == null || Tree.ContextMenu is not { } menu) return;
        item.IsSelected = true;
        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }

    void ExpandAll_Click(object sender, RoutedEventArgs e) => SetExpansion(expand: true);

    void CollapseAll_Click(object sender, RoutedEventArgs e) => SetExpansion(expand: false);

    void SetExpansion(bool expand)
    {
        switch (Tree.SelectedItem)
        {
            case RequestCollection col:
                col.IsExpandedInTree = expand;
                foreach (var folder in col.AllFolders) folder.IsExpandedInTree = expand;
                break;
            case Folder folder:
                foreach (var f in folder.SelfAndDescendants) f.IsExpandedInTree = expand;
                break;
        }
    }

    void OpenTab_Click(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is RequestItem req) OpenTab(req);
    }

    void RunRequest_Click(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is RequestItem req) OpenRunnerFor(req);
    }

    void NewRequest_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptWindow.Show(this, "Nueva request", "Nombre de la request:", "Nueva request");
        if (string.IsNullOrWhiteSpace(name)) return;
        var req = new RequestItem { Name = name.Trim() };

        switch (Tree.SelectedItem)
        {
            case RequestCollection col:
                col.Requests.Add(req);
                Storage.SaveCollection(col);
                break;
            case Folder folder:
                folder.Requests.Add(req);
                var owner = FindOwnerCollection(folder);
                if (owner != null) Storage.SaveCollection(owner);
                break;
            default:
                return;
        }
        OpenTab(req);
    }

    void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptWindow.Show(this, "Nueva carpeta", "Nombre de la carpeta:", "Nueva carpeta");
        if (string.IsNullOrWhiteSpace(name)) return;
        var newFolder = new Folder { Name = name.Trim() };

        switch (Tree.SelectedItem)
        {
            case RequestCollection col:
                col.Folders.Add(newFolder);
                Storage.SaveCollection(col);
                break;
            case Folder parent:
                parent.Folders.Add(newFolder);
                var owner = FindOwnerCollection(parent);
                if (owner != null) Storage.SaveCollection(owner);
                break;
        }
    }

    void Rename_Click(object sender, RoutedEventArgs e)
    {
        switch (Tree.SelectedItem)
        {
            case RequestCollection col:
                var colName = PromptWindow.Show(this, "Renombrar colección", "Nuevo nombre:", col.Name);
                if (!string.IsNullOrWhiteSpace(colName)) { col.Name = colName.Trim(); Storage.SaveCollection(col); }
                break;
            case Folder folder:
                var folderName = PromptWindow.Show(this, "Renombrar carpeta", "Nuevo nombre:", folder.Name);
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    folder.Name = folderName.Trim();
                    var owner = FindOwnerCollection(folder);
                    if (owner != null) Storage.SaveCollection(owner);
                }
                break;
            case RequestItem req:
                var reqName = PromptWindow.Show(this, "Renombrar request", "Nuevo nombre:", req.Name);
                if (!string.IsNullOrWhiteSpace(reqName))
                {
                    req.Name = reqName.Trim();
                    SaveOwnerOf(req);
                    var openTab = OpenTabs.OfType<RequestTab>().FirstOrDefault(t => t.Original == req);
                    if (openTab is { IsDirty: false }) openTab.SyncNameFromOriginal();
                }
                break;
        }
    }

    void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Tree.SelectedItem is not RequestItem req) return;
        var owner = FindOwner(req);
        if (owner == null) return;

        var copy = JsonSerializer.Deserialize<RequestItem>(JsonSerializer.Serialize(req))!;
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = req.Name + " (copia)";
        owner.Value.List.Insert(owner.Value.List.IndexOf(req) + 1, copy);
        Storage.SaveCollection(owner.Value.Collection);
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        switch (Tree.SelectedItem)
        {
            case RequestCollection col:
                if (MessageBox.Show(this, $"¿Eliminar la colección \"{col.Name}\" y todo su contenido?",
                        "Eliminar", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                CloseTabsOf(col.AllRequests);
                foreach (var colTab in OpenTabs.OfType<CollectionTab>().Where(t => t.Collection == col).ToList())
                    RemoveTab(colTab);
                Collections.Remove(col);
                Storage.DeleteCollection(col);
                break;

            case Folder folder:
                var folderParent = FindFolderParent(folder);
                if (folderParent == null) return;
                if (MessageBox.Show(this, $"¿Eliminar la carpeta \"{folder.Name}\" y todo su contenido?",
                        "Eliminar", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                CloseTabsOf(folder.AllRequests);
                folderParent.Value.List.Remove(folder);
                Storage.SaveCollection(folderParent.Value.Collection);
                break;

            case RequestItem req:
                var owner = FindOwner(req);
                if (owner == null) return;
                if (MessageBox.Show(this, $"¿Eliminar la request \"{req.Name}\"?",
                        "Eliminar", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                CloseTabsOf(new[] { req });
                owner.Value.List.Remove(req);
                Storage.SaveCollection(owner.Value.Collection);
                break;
        }
    }
}

public class TabTemplateSelector : DataTemplateSelector
{
    public DataTemplate? RequestTemplate { get; set; }
    public DataTemplate? CollectionTemplate { get; set; }
    public DataTemplate? RunnerTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) => item switch
    {
        CollectionTab => CollectionTemplate,
        RunnerTab => RunnerTemplate,
        _ => RequestTemplate
    };
}

public class AuthTypeToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            AuthType.Inherit => "Heredar de la colección",
            AuthType.None => "None (sin autenticación)",
            AuthType.Bearer => "Bearer Token",
            AuthType.Basic => "Basic Auth",
            AuthType.ApiKey => "API Key",
            _ => value?.ToString() ?? ""
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class TextToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class MethodToBrushConverter : IValueConverter
{
    static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
    static readonly Brush Peach = new SolidColorBrush(Color.FromRgb(0xFA, 0xB3, 0x87));
    static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA));
    static readonly Brush Mauve = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
    static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
    static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString()?.ToUpperInvariant() switch
        {
            "GET" => Green,
            "POST" => Peach,
            "PUT" => Blue,
            "PATCH" => Mauve,
            "DELETE" => Red,
            _ => Gray
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
