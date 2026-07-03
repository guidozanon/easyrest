using System.Windows;

namespace EasyRest;

public partial class PromptWindow : Window
{
    public PromptWindow()
    {
        InitializeComponent();
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    public static string? Show(Window owner, string title, string message, string initial = "")
    {
        var dlg = new PromptWindow { Owner = owner, Title = title };
        dlg.MessageText.Text = message;
        dlg.InputBox.Text = initial;
        dlg.InputBox.SelectAll();
        dlg.InputBox.Focus();
        return dlg.ShowDialog() == true ? dlg.InputBox.Text : null;
    }
}
