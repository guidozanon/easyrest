using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace EasyRest.Models;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Nodo mostrable en el árbol de colecciones, con estado transitorio de UI
/// (visibilidad según el filtro y expansión). No se persiste.</summary>
public abstract class TreeDisplayable : Observable
{
    bool _isVisibleInTree = true;
    bool _isExpandedInTree = true;

    [JsonIgnore]
    public bool IsVisibleInTree { get => _isVisibleInTree; set => Set(ref _isVisibleInTree, value); }

    [JsonIgnore]
    public bool IsExpandedInTree { get => _isExpandedInTree; set => Set(ref _isExpandedInTree, value); }
}

public class KeyValueItem : Observable
{
    bool _enabled = true;
    string _key = "";
    string _value = "";

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string Key { get => _key; set => Set(ref _key, value); }
    public string Value { get => _value; set => Set(ref _value, value); }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuthType
{
    /// <summary>Sin autenticación: no se envía nada, aunque la colección tenga auth configurada.</summary>
    None,
    Bearer,
    Basic,
    ApiKey,
    /// <summary>Usa la autenticación configurada en la colección. Solo válido en requests.</summary>
    Inherit
}

public class AuthConfig : Observable
{
    AuthType _type = AuthType.None;
    string _bearerToken = "";
    string _username = "";
    string _password = "";
    string _apiKeyName = "";
    string _apiKeyValue = "";
    string _apiKeyIn = "header";

    public AuthType Type { get => _type; set => Set(ref _type, value); }
    public string BearerToken { get => _bearerToken; set => Set(ref _bearerToken, value); }
    public string Username { get => _username; set => Set(ref _username, value); }
    public string Password { get => _password; set => Set(ref _password, value); }
    public string ApiKeyName { get => _apiKeyName; set => Set(ref _apiKeyName, value); }
    public string ApiKeyValue { get => _apiKeyValue; set => Set(ref _apiKeyValue, value); }
    public string ApiKeyIn { get => _apiKeyIn; set => Set(ref _apiKeyIn, value); }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BodyType { None, Json, Text, FormUrlEncoded }

public class BodyConfig : Observable
{
    BodyType _type = BodyType.None;
    string _raw = "";

    public BodyType Type { get => _type; set => Set(ref _type, value); }
    public string Raw { get => _raw; set => Set(ref _raw, value); }
    public ObservableCollection<KeyValueItem> FormItems { get; set; } = new();
}

public class RequestItem : TreeDisplayable
{
    string _name = "Nueva request";
    string _method = "GET";
    string _url = "";
    string _description = "";
    string _preRequestScript = "";
    string _testScript = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Method { get => _method; set => Set(ref _method, value); }
    public string Url { get => _url; set => Set(ref _url, value); }
    public string Description { get => _description; set => Set(ref _description, value); }

    /// <summary>JavaScript que corre antes de enviar (puede tocar request y variables).</summary>
    public string PreRequestScript { get => _preRequestScript; set => Set(ref _preRequestScript, value); }

    /// <summary>JavaScript que corre con la respuesta (asserts con er.test, extracción de variables).</summary>
    public string TestScript { get => _testScript; set => Set(ref _testScript, value); }
    public ObservableCollection<KeyValueItem> QueryParams { get; set; } = new();
    public ObservableCollection<KeyValueItem> Headers { get; set; } = new();
    public AuthConfig Auth { get; set; } = new() { Type = AuthType.Inherit };
    public BodyConfig Body { get; set; } = new();

    public override string ToString() => Name;
}

public class Folder : TreeDisplayable
{
    string _name = "Nueva carpeta";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => Set(ref _name, value); }
    public ObservableCollection<Folder> Folders { get; set; } = new();
    public ObservableCollection<RequestItem> Requests { get; set; } = new();

    [JsonIgnore]
    public IEnumerable<RequestItem> AllRequests =>
        Folders.SelectMany(f => f.AllRequests).Concat(Requests);

    /// <summary>Esta carpeta más todas sus descendientes.</summary>
    [JsonIgnore]
    public IEnumerable<Folder> SelfAndDescendants =>
        new[] { this }.Concat(Folders.SelectMany(f => f.SelfAndDescendants));

    public override string ToString() => Name;
}

public class RequestCollection : TreeDisplayable
{
    string _name = "Nueva colección";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => Set(ref _name, value); }
    public ObservableCollection<Folder> Folders { get; set; } = new();
    public ObservableCollection<RequestItem> Requests { get; set; } = new();

    /// <summary>Headers que heredan todas las requests de la colección (la request puede pisarlos por clave).</summary>
    public ObservableCollection<KeyValueItem> Headers { get; set; } = new();

    /// <summary>Autenticación que heredan las requests con Type = Inherit.</summary>
    public AuthConfig Auth { get; set; } = new();

    [JsonIgnore]
    public IEnumerable<RequestItem> AllRequests =>
        Folders.SelectMany(f => f.AllRequests).Concat(Requests);

    [JsonIgnore]
    public IEnumerable<Folder> AllFolders =>
        Folders.SelectMany(f => f.SelfAndDescendants);

    public override string ToString() => Name;
}

public class EnvironmentModel : Observable
{
    string _name = "Nuevo ambiente";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set => Set(ref _name, value); }
    public ObservableCollection<KeyValueItem> Variables { get; set; } = new();

    public override string ToString() => Name;
}

/// <summary>Un workspace: una carpeta (con sus colecciones y, opcionalmente, su propio repo git).</summary>
public class WorkspaceRef
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>Referencia a una pestaña abierta, para restaurarla al reabrir la app.</summary>
public class OpenTabRef
{
    /// <summary>"request" | "collection" | "runner".</summary>
    public string Kind { get; set; } = "";

    /// <summary>Id de la request o de la colección (vacío para el runner).</summary>
    public string? Id { get; set; }
}

public class AppSettings
{
    public string? ActiveEnvironmentId { get; set; }

    /// <summary>Legacy: workspace único. Se migra a la lista Workspaces al cargar.</summary>
    public string? WorkspacePath { get; set; }

    /// <summary>Workspaces registrados (además del "Personal" por defecto en AppData).</summary>
    public List<WorkspaceRef> Workspaces { get; set; } = new();

    /// <summary>Carpeta del workspace activo. Vacío/null = el "Personal" (AppData).</summary>
    public string? ActiveWorkspacePath { get; set; }

    /// <summary>Pestañas abiertas al cerrar, para restaurarlas al iniciar.</summary>
    public List<OpenTabRef> OpenTabs { get; set; } = new();

    /// <summary>Índice de la pestaña seleccionada al cerrar.</summary>
    public int SelectedTabIndex { get; set; }
}
