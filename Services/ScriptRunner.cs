using System.Text;
using System.Windows.Media;
using EasyRest.Models;
using Jint;

namespace EasyRest.Services;

public class ScriptTestResult
{
    public string Name { get; init; } = "";
    public bool Passed { get; init; }
    public string? Error { get; init; }

    // presentación (para la lista de tests de la UI)
    public string Icon => Passed ? "✔" : "✖";
    public Brush IconBrush => Passed ? PassBrush : FailBrush;
    static readonly Brush PassBrush = Frozen(0xA6, 0xE3, 0xA1);
    static readonly Brush FailBrush = Frozen(0xF3, 0x8B, 0xA8);

    static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public class ScriptResult
{
    public List<ScriptTestResult> Tests { get; } = new();
    public StringBuilder Log { get; } = new();
    public string? Error { get; set; }
}

/// <summary>Request mutable que ve el pre-request script (los cambios se aplican al envío,
/// no al modelo guardado). Los valores pueden seguir teniendo {{variables}}.</summary>
public class ScriptRequestProxy
{
    readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

#pragma warning disable IDE1006 // nombres en minúscula: es la API que ve JavaScript
    public string method { get; set; }
    public string url { get; set; }
    public string body { get; set; }

    public ScriptRequestProxy(string method, string url, string body, IEnumerable<(string Key, string Value)> headers)
    {
        this.method = method;
        this.url = url;
        this.body = body;
        foreach (var (key, value) in headers) _headers[key] = value;
    }

    public string? getHeader(string name) => _headers.TryGetValue(name, out var v) ? v : null;
    public void setHeader(string name, object? value) => _headers[name] = value?.ToString() ?? "";
    public void removeHeader(string name) => _headers.Remove(name);
#pragma warning restore IDE1006

    public List<KeyValueItem> ToHeaderList() =>
        _headers.Select(kv => new KeyValueItem { Key = kv.Key, Value = kv.Value }).ToList();
}

/// <summary>Respuesta de solo lectura que ve el post-response script.</summary>
public class ScriptResponseInfo
{
    readonly List<HeaderEntry> _headers;

#pragma warning disable IDE1006
    public int status { get; }
    public string body { get; }
    public double timeMs { get; }

    public ScriptResponseInfo(int status, string body, List<HeaderEntry> headers, double timeMs)
    {
        this.status = status;
        this.body = body;
        this.timeMs = timeMs;
        _headers = headers;
    }

    public string? getHeader(string name) =>
        _headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
#pragma warning restore IDE1006
}

/// <summary>API "er" que ven los scripts, más console.log.</summary>
public class ScriptApi
{
    readonly EnvironmentModel? _env;
    readonly ScriptResult _result;

    public ScriptApi(EnvironmentModel? env, ScriptResult result)
    {
        _env = env;
        _result = result;
    }

#pragma warning disable IDE1006
    public ScriptRequestProxy? request { get; init; }
    public ScriptResponseInfo? response { get; init; }

    public string? getVar(string name) =>
        _env?.Variables.FirstOrDefault(v => v.Enabled && string.Equals(v.Key?.Trim(), name, StringComparison.Ordinal))?.Value;

    public void setVar(string name, object? value)
    {
        if (_env == null)
        {
            _result.Log.AppendLine($"setVar('{name}'): no hay ambiente activo, se ignora.");
            return;
        }
        var text = value?.ToString() ?? "";
        var existing = _env.Variables.FirstOrDefault(v => string.Equals(v.Key?.Trim(), name, StringComparison.Ordinal));
        if (existing != null)
        {
            existing.Value = text;
            existing.Enabled = true;
        }
        else
        {
            _env.Variables.Add(new KeyValueItem { Key = name, Value = text });
        }
    }

    public void test(string name, bool condition) =>
        _result.Tests.Add(new ScriptTestResult { Name = name, Passed = condition });

    public void test(string name, Func<bool> condition)
    {
        try
        {
            _result.Tests.Add(new ScriptTestResult { Name = name, Passed = condition() });
        }
        catch (Exception ex)
        {
            _result.Tests.Add(new ScriptTestResult { Name = name, Passed = false, Error = ex.Message });
        }
    }

    public void log(object? value) => _result.Log.AppendLine(value?.ToString() ?? "null");
#pragma warning restore IDE1006
}

public class ConsoleProxy
{
    readonly ScriptResult _result;
    public ConsoleProxy(ScriptResult result) => _result = result;

#pragma warning disable IDE1006
    public void log(params object?[] args) =>
        _result.Log.AppendLine(string.Join(" ", args.Select(a => a?.ToString() ?? "null")));
#pragma warning restore IDE1006
}

public static class ScriptRunner
{
    public static ScriptResult Run(string script, EnvironmentModel? env,
        ScriptRequestProxy? request, ScriptResponseInfo? response)
    {
        var result = new ScriptResult();
        try
        {
            var engine = new Engine(options =>
            {
                options.TimeoutInterval(TimeSpan.FromSeconds(5));
                options.LimitRecursion(64);
                options.MaxStatements(200_000);
            });
            engine.SetValue("er", new ScriptApi(env, result) { request = request, response = response });
            engine.SetValue("console", new ConsoleProxy(result));
            engine.Execute(script);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        return result;
    }
}
