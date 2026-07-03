using EasyRest.Models;

namespace EasyRest;

/// <summary>Pestaña de configuración de una colección: nombre, headers heredados y auth heredada.</summary>
public class CollectionTab : Observable
{
    readonly Action _save;

    public CollectionTab(RequestCollection collection, Action save)
    {
        Collection = collection;
        _save = save;
    }

    public RequestCollection Collection { get; }

    public void Save() => _save();
}
