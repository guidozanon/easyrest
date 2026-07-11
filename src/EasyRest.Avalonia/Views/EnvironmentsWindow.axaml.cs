using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest.Avalonia.Views;

public partial class EnvironmentsWindow : Window
{
    ObservableCollection<EnvironmentModel> _environments = new();

    public EnvironmentsWindow() => InitializeComponent();

    public EnvironmentsWindow(ObservableCollection<EnvironmentModel> environments) : this()
    {
        _environments = environments;
        EnvList.ItemsSource = _environments;
        if (_environments.Count > 0) EnvList.SelectedIndex = 0;
    }

    void EnvList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var env = EnvList.SelectedItem as EnvironmentModel;
        VarsGrid.Bind(env?.Variables, "VARIABLE", "VALOR");
        VarsGrid.IsEnabled = env != null;
    }

    async void Add_Click(object? sender, RoutedEventArgs e)
    {
        var name = await Dialogs.Prompt(this, "Nuevo ambiente", "Nombre del ambiente:", "Desarrollo");
        if (string.IsNullOrWhiteSpace(name)) return;
        var env = new EnvironmentModel { Name = name.Trim() };
        _environments.Add(env);
        EnvList.SelectedItem = env;
    }

    async void Rename_Click(object? sender, RoutedEventArgs e)
    {
        if (EnvList.SelectedItem is not EnvironmentModel env) return;
        var name = await Dialogs.Prompt(this, "Renombrar ambiente", "Nuevo nombre:", env.Name);
        if (!string.IsNullOrWhiteSpace(name)) env.Name = name.Trim();
    }

    async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (EnvList.SelectedItem is not EnvironmentModel env) return;
        if (await Dialogs.Confirm(this, $"¿Eliminar el ambiente \"{env.Name}\"?", "Eliminar") == DialogResult.Yes)
            _environments.Remove(env);
    }

    // ----- Compartir / Importar -----

    void Share_Click(object? sender, RoutedEventArgs e)
    {
        if (EnvList.SelectedItem is not EnvironmentModel env) return;
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "Copiar al portapapeles" };
        copy.Click += async (_, _) => await CopyToClipboard(env, includeValues: true);
        var copyKeys = new MenuItem { Header = "Copiar sin valores (solo claves)" };
        copyKeys.Click += async (_, _) => await CopyToClipboard(env, includeValues: false);
        var save = new MenuItem { Header = "Guardar en archivo…" };
        save.Click += async (_, _) => await SaveToFile(env);
        menu.Items.Add(copy);
        menu.Items.Add(copyKeys);
        menu.Items.Add(save);
        menu.Open(sender as Control);
    }

    async Task CopyToClipboard(EnvironmentModel env, bool includeValues)
    {
        if (Clipboard != null)
            await Clipboard.SetTextAsync(EnvironmentShare.ToJson(env, includeValues));
    }

    async Task SaveToFile(EnvironmentModel env)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar ambiente",
            SuggestedFileName = env.Name + ".easyrest-env.json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });
        if (file == null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(EnvironmentShare.ToJson(env));
    }

    void Import_Click(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var paste = new MenuItem { Header = "Pegar desde el portapapeles" };
        paste.Click += async (_, _) =>
        {
            var text = Clipboard != null ? await Clipboard.GetTextAsync() : null;
            await ImportText(text, "El portapapeles no tiene un ambiente válido (JSON de EasyRest o Postman).");
        };
        var open = new MenuItem { Header = "Desde archivo…" };
        open.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importar ambiente",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
            });
            var path = files.FirstOrDefault()?.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;
            string text;
            try { text = await File.ReadAllTextAsync(path); }
            catch { text = ""; }
            await ImportText(text, "El archivo no tiene un ambiente válido (JSON de EasyRest o Postman).");
        };
        menu.Items.Add(paste);
        menu.Items.Add(open);
        menu.Open(sender as Control);
    }

    async Task ImportText(string? text, string errorMessage)
    {
        var env = EnvironmentShare.TryParse(text);
        if (env == null)
        {
            await Dialogs.Info(this, errorMessage, "Importar ambiente");
            return;
        }

        var existing = _environments.FirstOrDefault(x =>
            string.Equals(x.Name, env.Name, StringComparison.CurrentCultureIgnoreCase));
        if (existing != null)
        {
            var res = await Dialogs.Confirm(this,
                $"Ya existe un ambiente \"{existing.Name}\". ¿Reemplazar sus variables por las importadas?",
                "Importar ambiente");
            if (res == DialogResult.Cancel) return;
            if (res == DialogResult.Yes)
            {
                existing.Variables.Clear();
                foreach (var v in env.Variables) existing.Variables.Add(v);
                EnvList.SelectedItem = existing;
                return;
            }
            env.Name = UniqueName(env.Name);
        }
        _environments.Add(env);
        EnvList.SelectedItem = env;
    }

    string UniqueName(string name)
    {
        var n = 2;
        var candidate = $"{name} ({n})";
        while (_environments.Any(x => string.Equals(x.Name, candidate, StringComparison.CurrentCultureIgnoreCase)))
            candidate = $"{name} ({++n})";
        return candidate;
    }
}
