using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Xml.Linq;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest;

/// <summary>Estado de una pestaña abierta. El editor trabaja sobre un borrador (copia) de la
/// request: los cambios recién impactan en la colección (y en el árbol) al guardar. También
/// mantiene la respuesta de la última ejecución y la sincronización URL &lt;-&gt; query params.</summary>
public class RequestTab : Observable
{
    static readonly Brush GrayBrush = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86));
    static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
    static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));

    readonly Func<EnvironmentModel?> _envProvider;
    readonly Func<RequestCollection?> _ownerProvider;
    readonly Func<string> _pathProvider;
    readonly Func<bool> _save;
    bool _syncingQuery;
    bool _trackDirty;

    string _statusText = "";
    Brush _statusBrush = GrayBrush;
    string _timeText = "";
    string _sizeText = "";
    string _responseBody = "";
    string _responseHeaders = "";
    string _rawBody = "";
    string? _contentType;
    string _selectedFormat = "Auto";
    string _bodyJsonError = "";
    string _treeHint = "Ejecutá la request para ver el JSON de la respuesta como árbol.";
    bool _isSending;
    bool _isDirty;

    static readonly Regex VarTokenRegex = new(@"\{\{[^}]*\}\}", RegexOptions.Compiled);

    public RequestTab(RequestItem original, Func<EnvironmentModel?> envProvider,
        Func<RequestCollection?> ownerProvider, Func<string> pathProvider, Func<bool> save)
    {
        Original = original;
        Request = Clone(original);
        _envProvider = envProvider;
        _ownerProvider = ownerProvider;
        _pathProvider = pathProvider;
        _save = save;

        // Migración: requests guardadas antes de que exista la solapa Params
        if (Request.QueryParams.Count == 0 && Request.Url.Contains('?'))
            ParseUrlIntoParams();

        Request.PropertyChanged += Request_PropertyChanged;
        Request.QueryParams.CollectionChanged += QueryParams_CollectionChanged;
        foreach (var item in Request.QueryParams) item.PropertyChanged += QueryItem_PropertyChanged;

        HookDirtyTracking();
        ValidateBody();
    }

    /// <summary>La request real, la que vive en la colección y muestra el árbol.</summary>
    public RequestItem Original { get; }

    /// <summary>Borrador que edita la pestaña. Se aplica sobre Original al guardar.</summary>
    public RequestItem Request { get; }

    /// <summary>Colección dueña de la request (para heredar auth/headers).</summary>
    public RequestCollection? Owner => _ownerProvider();

    /// <summary>Colección › carpeta › … › nombre de la request.</summary>
    public string Breadcrumb
    {
        get
        {
            var path = _pathProvider();
            return path.Length > 0 ? $"{path} › {Request.Name}" : Request.Name;
        }
    }

    /// <summary>cURL de la request tal como se enviaría (variables resueltas, headers y auth heredados).</summary>
    public string ToCurl() => CurlHelper.ToCurl(Request, Owner, _envProvider());

    public bool IsDirty
    {
        get => _isDirty;
        private set => Set(ref _isDirty, value);
    }

    public string[] Formats { get; } = { "Auto", "JSON", "XML", "Texto" };

    public string SelectedFormat
    {
        get => _selectedFormat;
        set { if (Set(ref _selectedFormat, value)) ReformatBody(); }
    }

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public Brush StatusBrush { get => _statusBrush; set => Set(ref _statusBrush, value); }
    public string TimeText { get => _timeText; set => Set(ref _timeText, value); }
    public string SizeText { get => _sizeText; set => Set(ref _sizeText, value); }
    public string ResponseBody { get => _responseBody; set => Set(ref _responseBody, value); }
    public string ResponseHeaders { get => _responseHeaders; set => Set(ref _responseHeaders, value); }

    /// <summary>Mensaje de error si el body JSON está mal formado (vacío = válido).</summary>
    public string BodyJsonError { get => _bodyJsonError; set => Set(ref _bodyJsonError, value); }

    /// <summary>Árbol navegable de la respuesta JSON.</summary>
    public ObservableCollection<JsonTreeNode> ResponseTree { get; } = new();

    public string TreeHint { get => _treeHint; set => Set(ref _treeHint, value); }

    /// <summary>Headers de la respuesta como filas para la vista de tabla.</summary>
    public ObservableCollection<HeaderEntry> ResponseHeaderRows { get; } = new();

    /// <summary>Resultados de er.test() del post-response script.</summary>
    public ObservableCollection<ScriptTestResult> TestResults { get; } = new();

    string _testsHint = "Esta request no tiene script de tests.";
    public string TestsHint { get => _testsHint; set => Set(ref _testsHint, value); }

    string _scriptOutput = "";
    public string ScriptOutput { get => _scriptOutput; set => Set(ref _scriptOutput, value); }

    string _testsSummary = "";
    public string TestsSummary { get => _testsSummary; set => Set(ref _testsSummary, value); }

    Brush _testsBrush = GrayBrush;
    public Brush TestsBrush { get => _testsBrush; set => Set(ref _testsBrush, value); }

    public string[] BodyViews { get; } = { "Texto", "Árbol" };

    string _selectedBodyView = "Texto";
    public string SelectedBodyView
    {
        get => _selectedBodyView;
        set
        {
            if (!Set(ref _selectedBodyView, value)) return;
            Raise(nameof(BodyTextVisibility));
            Raise(nameof(BodyTreeVisibility));
        }
    }

    public System.Windows.Visibility BodyTextVisibility =>
        SelectedBodyView == "Texto" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility BodyTreeVisibility =>
        SelectedBodyView == "Árbol" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public string[] HeaderViews { get; } = { "Tabla", "Raw" };

    string _selectedHeaderView = "Tabla";
    public string SelectedHeaderView
    {
        get => _selectedHeaderView;
        set
        {
            if (!Set(ref _selectedHeaderView, value)) return;
            Raise(nameof(HeadersTableVisibility));
            Raise(nameof(HeadersRawVisibility));
        }
    }

    public System.Windows.Visibility HeadersTableVisibility =>
        SelectedHeaderView == "Tabla" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility HeadersRawVisibility =>
        SelectedHeaderView == "Raw" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public bool IsSending
    {
        get => _isSending;
        set { if (Set(ref _isSending, value)) Raise(nameof(IsNotSending)); }
    }

    public bool IsNotSending => !_isSending;

    /// <summary>Aplica el borrador sobre la request real y persiste la colección.</summary>
    public void Save()
    {
        Original.Name = Request.Name;
        Original.Method = Request.Method;
        Original.Url = Request.Url;
        Original.Description = Request.Description;
        Original.PreRequestScript = Request.PreRequestScript;
        Original.TestScript = Request.TestScript;

        Original.Auth.Type = Request.Auth.Type;
        Original.Auth.BearerToken = Request.Auth.BearerToken;
        Original.Auth.Username = Request.Auth.Username;
        Original.Auth.Password = Request.Auth.Password;
        Original.Auth.ApiKeyName = Request.Auth.ApiKeyName;
        Original.Auth.ApiKeyValue = Request.Auth.ApiKeyValue;
        Original.Auth.ApiKeyIn = Request.Auth.ApiKeyIn;

        Original.Body.Type = Request.Body.Type;
        Original.Body.Raw = Request.Body.Raw;

        CopyItems(Request.Headers, Original.Headers);
        CopyItems(Request.QueryParams, Original.QueryParams);
        CopyItems(Request.Body.FormItems, Original.Body.FormItems);

        // el guardado puede cancelarse (p. ej. una request nueva sin colección destino)
        if (_save()) IsDirty = false;
    }

    /// <summary>Marca la pestaña como sucia desde el arranque (requests nuevas sin guardar).</summary>
    public void MarkAsNew() => IsDirty = true;

    /// <summary>Recalcula el breadcrumb (p. ej. después de guardar una request nueva en una colección).</summary>
    public void RefreshBreadcrumb() => Raise(nameof(Breadcrumb));

    /// <summary>Interpreta un comando cURL y completa la request. Devuelve false si no se pudo parsear.</summary>
    public bool TryApplyCurl(string text)
    {
        if (!CurlHelper.TryParse(text, out var parsed)) return false;
        ApplyParsedCurl(parsed);
        return true;
    }

    /// <summary>Actualiza el nombre del borrador cuando renombran la request desde el árbol
    /// (solo tiene sentido si la pestaña no tiene cambios propios).</summary>
    public void SyncNameFromOriginal()
    {
        _trackDirty = false;
        Request.Name = Original.Name;
        _trackDirty = true;
    }

    public void Detach()
    {
        Request.PropertyChanged -= Request_PropertyChanged;
        Request.QueryParams.CollectionChanged -= QueryParams_CollectionChanged;
        foreach (var item in Request.QueryParams) item.PropertyChanged -= QueryItem_PropertyChanged;
    }

    static RequestItem Clone(RequestItem source) =>
        JsonSerializer.Deserialize<RequestItem>(JsonSerializer.Serialize(source))!;

    static void CopyItems(ObservableCollection<KeyValueItem> from, ObservableCollection<KeyValueItem> to)
    {
        to.Clear();
        foreach (var item in from)
            to.Add(new KeyValueItem { Enabled = item.Enabled, Key = item.Key, Value = item.Value });
    }

    // ----- Tracking de cambios (dirty) -----

    void HookDirtyTracking()
    {
        Request.PropertyChanged += (_, _) => MarkDirty();
        Request.Auth.PropertyChanged += (_, _) => MarkDirty();
        Request.Body.PropertyChanged += (_, _) => { MarkDirty(); ValidateBody(); };
        TrackList(Request.Headers);
        TrackList(Request.QueryParams);
        TrackList(Request.Body.FormItems);
        _trackDirty = true;
    }

    void TrackList(ObservableCollection<KeyValueItem> list)
    {
        foreach (var item in list) item.PropertyChanged += ItemDirty;
        list.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (KeyValueItem item in e.NewItems) item.PropertyChanged += ItemDirty;
            if (e.OldItems != null)
                foreach (KeyValueItem item in e.OldItems) item.PropertyChanged -= ItemDirty;
            MarkDirty();
        };
    }

    void ItemDirty(object? sender, PropertyChangedEventArgs e) => MarkDirty();

    void MarkDirty()
    {
        if (_trackDirty && !IsDirty) IsDirty = true;
    }

    // ----- Sincronización URL <-> query params (sobre el borrador) -----

    void Request_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RequestItem.Name)) Raise(nameof(Breadcrumb));
        if (_syncingQuery || e.PropertyName != nameof(RequestItem.Url)) return;

        // Si pegaron un comando cURL en la barra de URL, interpretarlo y completar la request
        var text = Request.Url;
        if (text.TrimStart().StartsWith("curl", StringComparison.OrdinalIgnoreCase) &&
            CurlHelper.TryParse(text, out var parsed))
        {
            ApplyParsedCurl(parsed);
            return;
        }

        ParseUrlIntoParams();
    }

    void ApplyParsedCurl(ParsedCurl parsed)
    {
        var knownMethods = new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };
        if (knownMethods.Contains(parsed.Method)) Request.Method = parsed.Method;

        Request.Headers.Clear();
        string? contentType = null;
        foreach (var (key, value) in parsed.Headers)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = value;
            Request.Headers.Add(new KeyValueItem { Key = key, Value = value });
        }

        if (parsed.Body != null)
        {
            var looksJson = parsed.Body.TrimStart().StartsWith('{') || parsed.Body.TrimStart().StartsWith('[') ||
                            contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
            Request.Body.Type = looksJson ? BodyType.Json : BodyType.Text;
            Request.Body.Raw = parsed.Body;
        }

        if (parsed.BasicUser != null)
        {
            Request.Auth.Type = AuthType.Basic;
            Request.Auth.Username = parsed.BasicUser;
            Request.Auth.Password = parsed.BasicPassword ?? "";
        }
        else if (parsed.Headers.Any(h => string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)))
        {
            // el Authorization viene como header explícito: que no se duplique con la auth heredada
            Request.Auth.Type = AuthType.None;
        }

        Request.Name = LastUrlSegment(parsed.Url);

        // Setear la URL parseada re-dispara este handler, pero ya no empieza con "curl"
        Request.Url = parsed.Url;
    }

    static string LastUrlSegment(string url)
    {
        var path = url.Split('?')[0].TrimEnd('/');
        var slash = path.LastIndexOf('/');
        var segment = slash >= 0 ? path[(slash + 1)..] : path;
        return segment.Length > 0 ? segment : "Nueva request";
    }

    void QueryParams_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (KeyValueItem item in e.NewItems) item.PropertyChanged += QueryItem_PropertyChanged;
        if (e.OldItems != null)
            foreach (KeyValueItem item in e.OldItems) item.PropertyChanged -= QueryItem_PropertyChanged;
        if (!_syncingQuery) RebuildUrl();
    }

    void QueryItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_syncingQuery) RebuildUrl();
    }

    void ParseUrlIntoParams()
    {
        _syncingQuery = true;
        try
        {
            var url = Request.Url;
            var qIndex = url.IndexOf('?');
            var query = qIndex >= 0 ? url[(qIndex + 1)..] : "";

            var disabled = Request.QueryParams.Where(q => !q.Enabled).ToList();
            foreach (var item in Request.QueryParams) item.PropertyChanged -= QueryItem_PropertyChanged;
            Request.QueryParams.Clear();

            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                Request.QueryParams.Add(new KeyValueItem
                {
                    Key = eq < 0 ? pair : pair[..eq],
                    Value = eq < 0 ? "" : pair[(eq + 1)..]
                });
            }
            foreach (var item in disabled) Request.QueryParams.Add(item);
        }
        finally
        {
            _syncingQuery = false;
        }
    }

    void RebuildUrl()
    {
        _syncingQuery = true;
        try
        {
            var url = Request.Url;
            var qIndex = url.IndexOf('?');
            var baseUrl = qIndex >= 0 ? url[..qIndex] : url;
            var parts = Request.QueryParams
                .Where(q => q.Enabled && !string.IsNullOrWhiteSpace(q.Key))
                .Select(q => string.IsNullOrEmpty(q.Value) ? q.Key.Trim() : $"{q.Key.Trim()}={q.Value}");
            var query = string.Join("&", parts);
            Request.Url = query.Length > 0 ? $"{baseUrl}?{query}" : baseUrl;
        }
        finally
        {
            _syncingQuery = false;
        }
    }

    // ----- Body JSON: validación y formateo -----

    /// <summary>Valida el body JSON reemplazando los {{tokens}} por un valor neutro,
    /// así un body templado como {"total": {{monto}}} no da falso positivo.</summary>
    void ValidateBody()
    {
        if (Request.Body.Type != BodyType.Json || string.IsNullOrWhiteSpace(Request.Body.Raw))
        {
            BodyJsonError = "";
            return;
        }
        var testable = VarTokenRegex.Replace(Request.Body.Raw, "0");
        try
        {
            using var _ = JsonDocument.Parse(testable);
            BodyJsonError = "";
        }
        catch (JsonException ex)
        {
            BodyJsonError = $"JSON inválido: {ex.Message}";
        }
    }

    /// <summary>Formatea el body JSON preservando las {{variables}} (incluso fuera de strings,
    /// usando números centinela que después se restauran).</summary>
    public void BeautifyBody()
    {
        if (Request.Body.Type != BodyType.Json || string.IsNullOrWhiteSpace(Request.Body.Raw)) return;
        var raw = Request.Body.Raw;

        if (TryFormat(raw, out var formatted))
        {
            Request.Body.Raw = formatted;
            return;
        }

        var tokens = new List<string>();
        var replaced = VarTokenRegex.Replace(raw, m =>
        {
            tokens.Add(m.Value);
            return Sentinel(tokens.Count - 1);
        });
        if (!TryFormat(replaced, out formatted)) return; // inválido de verdad: el error ya está visible

        for (var i = 0; i < tokens.Count; i++)
            formatted = formatted.Replace(Sentinel(i), tokens[i]);
        Request.Body.Raw = formatted;
    }

    static string Sentinel(int i) => $"81249{i:000}77361";

    static bool TryFormat(string json, out string formatted)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            formatted = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            return true;
        }
        catch
        {
            formatted = "";
            return false;
        }
    }

    // ----- Envío (usa el borrador: se envía lo que se ve, guardado o no) -----

    public async Task SendAsync()
    {
        if (IsSending) return;

        ValidateBody();
        if (Request.Body.Type == BodyType.Json && BodyJsonError.Length > 0)
        {
            StatusText = "JSON inválido";
            StatusBrush = RedBrush;
            ResponseBody = BodyJsonError + "\n\nCorregí el body antes de enviar.";
            ResponseTree.Clear();
            TreeHint = "La respuesta no es JSON.";
            return;
        }

        IsSending = true;
        StatusText = "Enviando…";
        StatusBrush = GrayBrush;
        TimeText = "";
        SizeText = "";
        ResponseBody = "";
        ResponseHeaders = "";
        _rawBody = "";
        _contentType = null;

        try
        {
            var r = await HttpExecutor.ExecuteAsync(Request, Owner, _envProvider());

            ResponseTree.Clear();
            ResponseHeaderRows.Clear();
            ApplyScriptResults(r);
            if (r.Error != null)
            {
                StatusText = "Error";
                StatusBrush = RedBrush;
                ResponseBody = r.Error;
                TimeText = $"{r.ElapsedMs} ms";
                TreeHint = "La respuesta no es JSON.";
            }
            else
            {
                foreach (var header in r.HeaderList) ResponseHeaderRows.Add(header);
                StatusText = $"{r.StatusCode} {r.StatusText}";
                StatusBrush = r.StatusCode < 400 ? GreenBrush : RedBrush;
                TimeText = $"{r.ElapsedMs} ms";
                SizeText = FormatSize(r.SizeBytes);
                _rawBody = r.Body;
                _contentType = r.ContentType;
                ResponseHeaders = r.HeadersText;
                ReformatBody();

                var nodes = JsonTree.TryBuild(_rawBody);
                if (nodes != null)
                {
                    foreach (var node in nodes) ResponseTree.Add(node);
                    TreeHint = "";
                }
                else
                {
                    TreeHint = "La respuesta no es JSON.";
                }
            }
        }
        finally
        {
            IsSending = false;
        }
    }

    void ApplyScriptResults(ResponseResult r)
    {
        TestResults.Clear();
        ScriptOutput = r.ScriptLog;
        if (r.ScriptError != null)
            ScriptOutput = (ScriptOutput.Length > 0 ? ScriptOutput + "\n" : "") + "Error en el script: " + r.ScriptError;

        if (r.ScriptTests is { Count: > 0 } tests)
        {
            foreach (var t in tests) TestResults.Add(t);
            var passed = tests.Count(t => t.Passed);
            TestsSummary = $"Tests: {passed}/{tests.Count}";
            TestsBrush = passed == tests.Count ? GreenBrush : RedBrush;
            TestsHint = "";
        }
        else
        {
            TestsSummary = "";
            TestsHint = r.ScriptError != null
                ? "El script de tests falló (mirá la consola de abajo)."
                : string.IsNullOrWhiteSpace(Request.TestScript)
                    ? "Esta request no tiene script de tests."
                    : "El script corrió pero no registró tests (usá er.test(nombre, condición)).";
        }
    }

    void ReformatBody() => ResponseBody = FormatBody(_rawBody, _contentType, SelectedFormat);

    internal static string FormatBody(string raw, string? contentType, string format)
    {
        if (raw.Length == 0) return raw;
        var effective = format;
        if (format == "Auto")
        {
            effective = contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ? "JSON"
                : contentType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true ? "XML"
                : "Texto";
        }
        return effective switch
        {
            "JSON" => PrettyJson(raw),
            "XML" => PrettyXml(raw),
            _ => raw
        };
    }

    static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.##} MB"
    };

    static string PrettyJson(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return body;
        }
    }

    static string PrettyXml(string body)
    {
        try
        {
            return XDocument.Parse(body).ToString();
        }
        catch
        {
            return body;
        }
    }
}
