using System.Collections.ObjectModel;
using System.Windows;
using EasyRest.Models;

namespace EasyRest;

/// <summary>Selector de destino (colección o carpeta) para guardar una request nueva.</summary>
public partial class SaveTargetWindow : Window
{
    public class SaveTarget
    {
        public string Display { get; init; } = "";
        public RequestCollection Collection { get; init; } = null!;
        public Folder? Folder { get; init; }
        public override string ToString() => Display;
    }

    public SaveTargetWindow(ObservableCollection<RequestCollection> collections)
    {
        InitializeComponent();
        var targets = new List<SaveTarget>();
        foreach (var col in collections)
        {
            targets.Add(new SaveTarget { Display = col.Name, Collection = col });
            AddFolders(targets, col, col.Folders, col.Name);
        }
        TargetCombo.ItemsSource = targets;
        if (targets.Count > 0) TargetCombo.SelectedIndex = 0;
    }

    static void AddFolders(List<SaveTarget> targets, RequestCollection col,
        IEnumerable<Folder> folders, string prefix)
    {
        foreach (var folder in folders)
        {
            var path = $"{prefix} › {folder.Name}";
            targets.Add(new SaveTarget { Display = path, Collection = col, Folder = folder });
            AddFolders(targets, col, folder.Folders, path);
        }
    }

    public SaveTarget? Selected => TargetCombo.SelectedItem as SaveTarget;

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = Selected != null;
}
