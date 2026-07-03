using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using EasyRest.Models;

namespace EasyRest.Avalonia.Views;

public partial class RequestEditor : UserControl
{
    string _nameBeforeEdit = "";

    public RequestEditor()
    {
        InitializeComponent();
        MethodCombo.ItemsSource = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };
        AuthTypeCombo.ItemsSource = new[]
            { AuthType.Inherit, AuthType.None, AuthType.Bearer, AuthType.Basic, AuthType.ApiKey };
        ApiKeyInCombo.ItemsSource = new[] { "header", "query" };
        BodyTypeCombo.ItemsSource = Enum.GetValues<BodyType>();
        ScriptViewCombo.ItemsSource = new[] { "Pre-request", "Post-response (tests)" };
        ScriptViewCombo.SelectedIndex = 0;

        DataContextChanged += (_, _) => { UpdatePanels(); BindGrids(); };
        UrlBox.AddHandler(TextInputEvent, UrlBox_TextInput, RoutingStrategies.Tunnel);
        UrlBox.PastingFromClipboard += UrlBox_Pasting;
    }

    RequestTab? Vm => DataContext as RequestTab;

    void UpdatePanels()
    {
        UpdateAuthPanels();
        UpdateBodyPanels();
        ScriptView_Changed(this, null);
    }

    void BindGrids()
    {
        KvGrid.Bind(ParamsGrid, Vm?.Request.QueryParams);
        KvGrid.Bind(HeadersGrid, Vm?.Request.Headers);
        KvGrid.Bind(FormGrid, Vm?.Request.Body.FormItems);
    }

    // ----- Auth -----

    void AuthType_Changed(object? sender, SelectionChangedEventArgs? e) => UpdateAuthPanels();

    void UpdateAuthPanels()
    {
        var type = Vm?.Request.Auth.Type ?? AuthType.Inherit;
        BearerPanel.IsVisible = type == AuthType.Bearer;
        BasicPanel.IsVisible = type == AuthType.Basic;
        ApiKeyPanel.IsVisible = type == AuthType.ApiKey;

        InheritHint.IsVisible = type is AuthType.Inherit or AuthType.None;
        if (type == AuthType.Inherit)
        {
            var collectionAuth = Vm?.Owner?.Auth.Type ?? AuthType.None;
            InheritHint.Text = collectionAuth is AuthType.None or AuthType.Inherit
                ? "La colección no tiene autenticación configurada: no se envía auth."
                : $"Se envía la autenticación de la colección: {collectionAuth}.";
        }
        else if (type == AuthType.None)
        {
            InheritHint.Text = "No se envía autenticación, aunque la colección tenga una configurada.";
        }
    }

    // ----- Body -----

    void BodyType_Changed(object? sender, SelectionChangedEventArgs e) => UpdateBodyPanels();

    void UpdateBodyPanels()
    {
        var type = Vm?.Request.Body.Type ?? BodyType.None;
        BodyRawBox.IsVisible = type is BodyType.Json or BodyType.Text;
        FormGrid.IsVisible = type == BodyType.FormUrlEncoded;
        BeautifyBtn.IsVisible = type == BodyType.Json;
    }

    void Beautify_Click(object? sender, RoutedEventArgs e) => Vm?.BeautifyBody();

    // ----- Scripts -----

    void ScriptView_Changed(object? sender, SelectionChangedEventArgs? e)
    {
        if (PreScriptBox == null) return;
        var pre = ScriptViewCombo.SelectedIndex == 0;
        PreScriptBox.IsVisible = pre;
        TestScriptBox.IsVisible = !pre;
        ScriptHint.Text = pre
            ? "Corre antes de enviar. API: er.request.url / .method / .body / .setHeader(k,v) · " +
              "er.getVar(n) / er.setVar(n,v) · console.log(...)"
            : "Corre con la respuesta. API: er.response.status / .body / .timeMs / .getHeader(k) · " +
              "er.test(nombre, condición) · er.setVar(n,v) · JSON.parse(er.response.body) · console.log(...)";
    }

    // ----- cURL al pegar en la URL -----

    void UrlBox_TextInput(object? sender, TextInputEventArgs e) { }

    async void UrlBox_Pasting(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null || Vm == null) return;
        var text = await clipboard.GetTextAsync();
        if (text == null || !text.TrimStart().StartsWith("curl", StringComparison.OrdinalIgnoreCase)) return;
        if (Vm.TryApplyCurl(text) && e is RoutedEventArgs re) re.Handled = true;
    }

    // ----- Rename inline -----

    void Breadcrumb_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm == null) return;
        _nameBeforeEdit = Vm.Request.Name;
        NameEditBox.Text = Vm.Request.Name;
        BreadcrumbText.IsVisible = false;
        NameEditBox.IsVisible = true;
        NameEditBox.Focus();
        NameEditBox.SelectAll();
    }

    void CommitNameEdit(bool cancel)
    {
        if (!NameEditBox.IsVisible || Vm == null) return;
        var newName = NameEditBox.Text?.Trim() ?? "";
        Vm.Request.Name = cancel || newName.Length == 0 ? _nameBeforeEdit : newName;
        NameEditBox.IsVisible = false;
        BreadcrumbText.IsVisible = true;
    }

    void NameEdit_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitNameEdit(false); e.Handled = true; }
        else if (e.Key == Key.Escape) { CommitNameEdit(true); e.Handled = true; }
    }

    void NameEdit_LostFocus(object? sender, RoutedEventArgs e) => CommitNameEdit(false);

    // ----- Acciones -----

    async void Send_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.SendAsync();
    }

    void Save_Click(object? sender, RoutedEventArgs e) => Vm?.Save();

    void OpenRunner_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm != null && this.FindAncestorOfType<MainWindow>() is { } main)
            main.OpenRunner(Vm.Original);
    }

    async void Share_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "Copiar cURL" };
        copy.Click += async (_, _) =>
        {
            var cb = TopLevel.GetTopLevel(this)?.Clipboard;
            if (cb != null) await cb.SetTextAsync(Vm.ToCurl());
        };
        var save = new MenuItem { Header = "Guardar cURL en archivo…" };
        save.Click += async (_, _) => await SaveText(Vm.ToCurl(), Vm.Request.Name + ".curl.txt");
        menu.Items.Add(copy);
        menu.Items.Add(save);
        menu.Open(sender as Control);
    }

    async void SaveResponse_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.ResponseBody.Length == 0) return;
        var json = Vm.ResponseBody.TrimStart().StartsWith('{') || Vm.ResponseBody.TrimStart().StartsWith('[');
        await SaveText(Vm.ResponseBody, Vm.Request.Name + (json ? ".json" : ".txt"));
    }

    async Task SaveText(string content, string suggestedName)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName
        });
        if (file == null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
    }
}
