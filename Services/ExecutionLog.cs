using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace EasyRest.Services;

public class LogEntry
{
    public string Time { get; init; } = "";
    public string Method { get; init; } = "";
    public string Url { get; init; } = "";
    public string Status { get; init; } = "";
    public Brush StatusBrush { get; init; } = Brushes.Gray;
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

    static readonly Brush GreenBrush = Frozen(0xA6, 0xE3, 0xA1);
    static readonly Brush RedBrush = Frozen(0xF3, 0x8B, 0xA8);
    static readonly Brush GrayBrush = Frozen(0x6C, 0x70, 0x86);

    static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze(); // usable desde cualquier thread
        return brush;
    }

    public static ObservableCollection<LogEntry> Entries { get; } = new();

    public static void Record(string method, string url, ResponseResult result,
        string requestHeaders, string requestBody)
    {
        string status;
        Brush brush;
        if (result.Error != null) { status = "ERROR"; brush = RedBrush; }
        else if (result.StatusCode == 0) { status = "—"; brush = GrayBrush; }
        else
        {
            status = $"{result.StatusCode} {result.StatusText}";
            brush = result.StatusCode < 400 ? GreenBrush : RedBrush;
        }

        var meta = $"{result.ElapsedMs} ms";
        if (result.SizeBytes > 0) meta += $" · {FormatSize(result.SizeBytes)}";

        var entry = new LogEntry
        {
            Time = DateTime.Now.ToString("HH:mm:ss"),
            Method = method,
            Url = url,
            Status = status,
            StatusBrush = brush,
            Meta = meta,
            RequestHeaders = requestHeaders,
            RequestBody = Truncate(requestBody),
            ResponseHeaders = result.HeadersText,
            ResponseBody = Truncate(result.Error ?? result.Body)
        };

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) { Add(entry); return; }
        if (dispatcher.CheckAccess()) Add(entry);
        else dispatcher.Invoke(() => Add(entry));
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
