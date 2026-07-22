using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace EasyRest.Avalonia;

public enum DialogResult { Yes, No, Cancel }

/// <summary>Diálogos modales simples (Avalonia no trae MessageBox).</summary>
public static class Dialogs
{
    static IBrush Bg => Brush.Parse("#1E1E2E");
    static IBrush Text => Brush.Parse("#CDD6F4");

    public static async Task<DialogResult> Confirm(Window owner, string message, string title,
        bool withCancel = true)
    {
        var result = DialogResult.Cancel;
        var dlg = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Bg
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var yes = new Button { Content = "Sí", MinWidth = 90 };
        var no = new Button { Content = "No", MinWidth = 90 };
        yes.Classes.Add("accent");
        yes.Click += (_, _) => { result = DialogResult.Yes; dlg.Close(); };
        no.Click += (_, _) => { result = DialogResult.No; dlg.Close(); };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        if (withCancel)
        {
            var cancel = new Button { Content = "Cancelar", MinWidth = 90 };
            cancel.Click += (_, _) => { result = DialogResult.Cancel; dlg.Close(); };
            buttons.Children.Add(cancel);
        }

        dlg.Content = new StackPanel
        {
            Margin = new(18),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Text },
                buttons
            }
        };

        await dlg.ShowDialog(owner);
        return result;
    }

    /// <summary>Diálogo con botones de texto personalizado (más «Cancelar»). Devuelve el índice
    /// de la opción elegida, o null si se cancela.</summary>
    public static async Task<int?> Choice(Window owner, string title, string message,
        params string[] options)
    {
        int? result = null;
        var dlg = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Bg
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        for (var i = 0; i < options.Length; i++)
        {
            var index = i;
            var btn = new Button { Content = options[i], MinWidth = 90 };
            if (i == 0) btn.Classes.Add("accent");
            btn.Click += (_, _) => { result = index; dlg.Close(); };
            buttons.Children.Add(btn);
        }
        var cancel = new Button { Content = "Cancelar", MinWidth = 90 };
        cancel.Click += (_, _) => { result = null; dlg.Close(); };
        buttons.Children.Add(cancel);

        dlg.Content = new StackPanel
        {
            Margin = new(18),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Text },
                buttons
            }
        };

        await dlg.ShowDialog(owner);
        return result;
    }

    public static async Task Info(Window owner, string message, string title)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Bg
        };
        var ok = new Button { Content = "Aceptar", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Classes.Add("accent");
        ok.Click += (_, _) => dlg.Close();
        dlg.Content = new StackPanel
        {
            Margin = new(18),
            Spacing = 16,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Text }, ok }
        };
        await dlg.ShowDialog(owner);
    }

    public static async Task<string?> Prompt(Window owner, string title, string message, string initial = "")
    {
        string? result = null;
        var dlg = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Bg
        };
        var box = new TextBox { Text = initial };
        var ok = new Button { Content = "Aceptar", MinWidth = 90 };
        var cancel = new Button { Content = "Cancelar", MinWidth = 90 };
        ok.Classes.Add("accent");
        ok.Click += (_, _) => { result = box.Text; dlg.Close(); };
        cancel.Click += (_, _) => { result = null; dlg.Close(); };
        dlg.Content = new StackPanel
        {
            Margin = new(18),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = message, Foreground = Text },
                box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { ok, cancel }
                }
            }
        };
        dlg.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        await dlg.ShowDialog(owner);
        return result;
    }
}
