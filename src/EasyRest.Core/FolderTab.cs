using EasyRest.Models;

namespace EasyRest;

/// <summary>Pestaña de configuración de una carpeta: nombre y auth heredada. La auth de la carpeta
/// se aplica a las requests (y subcarpetas) que heredan, salvo que un nivel más interno la pise.</summary>
public class FolderTab : Observable
{
    readonly Action _save;

    public FolderTab(Folder folder, Action save)
    {
        Folder = folder;
        _save = save;
    }

    public Folder Folder { get; }

    public void Save() => _save();
}
