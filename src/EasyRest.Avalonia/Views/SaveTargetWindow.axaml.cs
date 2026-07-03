using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyRest.Models;

namespace EasyRest.Avalonia.Views;

public partial class SaveTargetWindow : Window
{
    public class SaveTarget
    {
        public string Display { get; init; } = "";
        public RequestCollection Collection { get; init; } = null!;
        public Folder? Folder { get; init; }
        public override string ToString() => Display;
    }

    public SaveTarget? Selected { get; private set; }

    public SaveTargetWindow() => InitializeComponent();

    public SaveTargetWindow(ObservableCollection<RequestCollection> collections) : this()
    {
        var targets = new List<SaveTarget>();
        foreach (var col in collections)
        {
            targets.Add(new SaveTarget { Display = col.Name, Collection = col });
            AddFolders(targets, col, col.Folders, col.Name);
        }
        TargetCombo.ItemsSource = targets;
        if (targets.Count > 0) TargetCombo.SelectedIndex = 0;
    }

    static void AddFolders(List<SaveTarget> targets, RequestCollection col, IEnumerable<Folder> folders, string prefix)
    {
        foreach (var folder in folders)
        {
            var path = $"{prefix} › {folder.Name}";
            targets.Add(new SaveTarget { Display = path, Collection = col, Folder = folder });
            AddFolders(targets, col, folder.Folders, path);
        }
    }

    void Ok_Click(object? sender, RoutedEventArgs e)
    {
        Selected = TargetCombo.SelectedItem as SaveTarget;
        Close();
    }

    void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
