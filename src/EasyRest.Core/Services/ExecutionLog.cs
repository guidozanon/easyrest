using System.Collections.ObjectModel;

namespace EasyRest.Services;

public enum LogKind { Neutral, Success, Failure }

public class LogEntry
{
    public string Time { get; init; } = "";
    public string Method { get; init; } = "";
    public string Url { get; init; } = "";
    public string Status { get; init; } = "";
    public LogKind Kind { get; init; } = LogKind.Neutral;
    public string Meta { get; init; } = "";
    public string RequestHeaders { get; init; } = "";
    public string RequestBody { get; init; } = "";
    public string ResponseHeaders { get; init; } = "";
    public string ResponseBody { get; init; } = "";
}

/// <summary>Registro en memoria de todas las requests ejecutadas (editor y runner).</summary>
public static class ExecutionLog
{
    const int MaxEntries = 300;
    const int MaxBodyChars = 200_000;

    /// <summary>Marshaler al thread de UI. Lo setea cada head (WPF/Avalonia) al arrancar;
    /// sin setear, las entradas se agregan en el thread que llama.</summary>
    public static Action<Action>? Marshal { get; set; }

    public static ObservableCollection<LogEntry> Entries { get; } = new();

    public static void Record(string method, string url, ResponseResult result,
        string requestHeaders, string requestBody)
    {
        string status;
        LogKind kind;
        if (result.Error != null) { status = "ERROR"; kind = LogKind.Failure; }
        else if (result.StatusCode == 0) { status = "—"; kind = LogKind.Neutral; }
        else
        {
            status = $"{result.StatusCode} {result.StatusText}";
            kind = result.StatusCode < 400 ? LogKind.Success : LogKind.Failure;
        }

        var meta = $"{result.ElapsedMs} ms";
        if (result.SizeBytes > 0) meta += $" · {FormatSize(result.SizeBytes)}";

        var entry = new LogEntry
        {
            Time = DateTime.Now.ToString("HH:mm:ss"),
            Method = method,
            Url = url,
            Status = status,
            Kind = kind,
            Meta = meta,
            RequestHeaders = requestHeaders,
            RequestBody = Truncate(requestBody),
            ResponseHeaders = result.HeadersText,
            ResponseBody = Truncate(result.Error ?? result.Body)
        };

        if (Marshal != null) Marshal(() => Add(entry));
        else Add(entry);
    }

    static void Add(LogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
    }

    static string Truncate(string s) =>
        s.Length <= MaxBodyChars ? s : s[..MaxBodyChars] + $"\n… (truncado, {s.Length} caracteres en total)";

    static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.##} MB"
    };
}
