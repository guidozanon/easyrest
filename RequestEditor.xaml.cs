using System.Windows;
using System.Windows.Controls;
using EasyRest.Models;

namespace EasyRest;

public partial class RequestEditor : UserControl
{
    public RequestEditor()
    {
        InitializeComponent();
        MethodCombo.ItemsSource = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };
        AuthTypeCombo.ItemsSource = new[]
            { AuthType.Inherit, AuthType.None, AuthType.Bearer, AuthType.Basic, AuthType.ApiKey };
        BodyTypeCombo.ItemsSource = Enum.GetValues(typeof(BodyType));
        ApiKeyInCombo.ItemsSource = new[] { "header", "query" };
        ScriptViewCombo.ItemsSource = new[] { "Pre-request", "Post-response (tests)" };
        ScriptViewCombo.SelectedIndex = 0;
        DataContextChanged += (_, _) => UpdatePanels();

        // Interceptar el paste en la URL: un cURL multilínea se truncaría en el TextBox single-line
        DataObject.AddPastingHandler(UrlBox, UrlBox_Pasting);
    }

    void UrlBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text) return;
        if (!text.TrimStart().StartsWith("curl", StringComparison.OrdinalIgnoreCase)) return;
        if (Vm?.TryApplyCurl(text) == true) e.CancelCommand();
    }

    // ----- Edición inline del nombre (doble click sobre el breadcrumb) -----

    string _nameBeforeEdit = "";

    void Breadcrumb_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || Vm == null) return;
        _nameBeforeEdit = Vm.Request.Name;
        NameEditBox.Text = Vm.Request.Name;
        BreadcrumbText.Visibility = Visibility.Collapsed;
        NameEditBox.Visibility = Visibility.Visible;
        NameEditBox.Focus();
        NameEditBox.SelectAll();
        e.Handled = true;
    }

    void CommitNameEdit(bool cancel)
    {
        if (NameEditBox.Visibility != Visibility.Visible || Vm == null) return;
        var newName = NameEditBox.Text.Trim();
        Vm.Request.Name = cancel || newName.Length == 0 ? _nameBeforeEdit : newName;
        NameEditBox.Visibility = Visibility.Collapsed;
        BreadcrumbText.Visibility = Visibility.Visible;
    }

    void NameEdit_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case System.Windows.Input.Key.Enter:
                CommitNameEdit(cancel: false);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Escape:
                CommitNameEdit(cancel: true);
                e.Handled = true;
                break;
        }
    }

    void NameEdit_LostFocus(object sender, RoutedEventArgs e) => CommitNameEdit(cancel: false);

    RequestTab? Vm => DataContext as RequestTab;

    void UpdatePanels()
    {
        UpdateAuthPanels();
        UpdateBodyPanels();
    }

    void AuthType_Changed(object sender, SelectionChangedEventArgs e) => UpdateAuthPanels();

    void UpdateAuthPanels()
    {
        var type = Vm?.Request.Auth.Type ?? AuthType.Inherit;
        BearerPanel.Visibility = type == AuthType.Bearer ? Visibility.Visible : Visibility.Collapsed;
        BasicPanel.Visibility = type == AuthType.Basic ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyPanel.Visibility = type == AuthType.ApiKey ? Visibility.Visible : Visibility.Collapsed;

        switch (type)
        {
            case AuthType.Inherit:
                var collectionAuth = Vm?.Owner?.Auth.Type ?? AuthType.None;
                InheritHint.Text = collectionAuth is AuthType.None or AuthType.Inherit
                    ? "La colección no tiene autenticación configurada: no se envía auth."
                    : $"Se envía la autenticación de la colección: {collectionAuth}.";
                InheritHint.Visibility = Visibility.Visible;
                break;
            case AuthType.None:
                InheritHint.Text = "No se envía autenticación, aunque la colección tenga una configurada.";
                InheritHint.Visibility = Visibility.Visible;
                break;
            default:
                InheritHint.Visibility = Visibility.Collapsed;
                break;
        }
    }

    void BodyType_Changed(object sender, SelectionChangedEventArgs e) => UpdateBodyPanels();

    void UpdateBodyPanels()
    {
        var type = Vm?.Request.Body.Type ?? BodyType.None;
        BodyRawBox.Visibility = type is BodyType.Json or BodyType.Text ? Visibility.Visible : Visibility.Collapsed;
        FormGrid.Visibility = type == BodyType.FormUrlEncoded ? Visibility.Visible : Visibility.Collapsed;
        BeautifyBtn.Visibility = type == BodyType.Json ? Visibility.Visible : Visibility.Collapsed;
    }

    void Beautify_Click(object sender, RoutedEventArgs e) => Vm?.BeautifyBody();

    void ScriptView_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PreScriptBox == null) return;
        var pre = ScriptViewCombo.SelectedIndex == 0;
        PreScriptBox.Visibility = pre ? Visibility.Visible : Visibility.Collapsed;
        TestScriptBox.Visibility = pre ? Visibility.Collapsed : Visibility.Visible;
        ScriptHint.Text = pre
            ? "Corre antes de enviar. API: er.request.url / .method / .body / .setHeader(k,v) · " +
              "er.getVar(n) / er.setVar(n,v) · console.log(...)"
            : "Corre con la respuesta. API: er.response.status / .body / .timeMs / .getHeader(k) · " +
              "er.test(nombre, condición) · er.setVar(n,v) · JSON.parse(er.response.body) · console.log(...)";
    }

    async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) await Vm.SendAsync();
    }

    void Save_Click(object sender, RoutedEventArgs e) => Vm?.Save();

    void OpenRunner_Click(object sender, RoutedEventArgs e)
    {
        // Original, no el borrador: el runner busca la request dentro de la colección
        if (Vm != null && Window.GetWindow(this) is MainWindow main)
            main.OpenRunnerFor(Vm.Original);
    }

    void Share_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    void CopyCurl_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        try
        {
            Clipboard.SetText(Vm.ToCurl());
        }
        catch
        {
            // el clipboard puede estar tomado por otro proceso: reintento simple
            try { Clipboard.SetDataObject(Vm.ToCurl()); } catch { /* nada más que hacer */ }
        }
    }

    void SaveCurl_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Guardar cURL",
            FileName = SanitizeFileName(Vm.Request.Name) + ".curl.txt",
            Filter = "Texto (*.txt)|*.txt|Todos los archivos|*.*"
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
            System.IO.File.WriteAllText(dlg.FileName, Vm.ToCurl());
    }

    void SaveResponse_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null || Vm.ResponseBody.Length == 0) return;
        var isJson = Vm.ResponseBody.TrimStart().StartsWith('{') || Vm.ResponseBody.TrimStart().StartsWith('[');
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Guardar respuesta",
            FileName = SanitizeFileName(Vm.Request.Name) + (isJson ? ".json" : ".txt"),
            Filter = "JSON (*.json)|*.json|Texto (*.txt)|*.txt|Todos los archivos|*.*",
            FilterIndex = isJson ? 1 : 2
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
            System.IO.File.WriteAllText(dlg.FileName, Vm.ResponseBody);
    }

    static string SanitizeFileName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "request" : name.Trim();
    }
}
