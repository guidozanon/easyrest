using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using EasyRest.Models;

namespace EasyRest.Services;

public class HeaderEntry
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public class ResponseResult
{
    public int StatusCode { get; set; }
    public string StatusText { get; set; } = "";
    public string Body { get; set; } = "";
    public string HeadersText { get; set; } = "";
    public List<HeaderEntry> HeaderList { get; } = new();
    public string? ContentType { get; set; }
    public long ElapsedMs { get; set; }
    public long SizeBytes { get; set; }
    public bool IsSuccess { get; set; }
    public string? Error { get; set; }

    // resultados de los scripts
    public List<ScriptTestResult>? ScriptTests { get; set; }
    public string ScriptLog { get; set; } = "";
    public string? ScriptError { get; set; }
}

public static class HttpExecutor
{
    static readonly HttpClient Client = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    /// <summary>Headers efectivos: los de la request más los heredados de la colección
    /// (si la request ya define la clave, gana la request).</summary>
    public static List<KeyValueItem> EffectiveHeaders(RequestItem req, RequestCollection? collection)
    {
        var headers = req.Headers
            .Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key))
            .ToList();
        if (collection != null)
            foreach (var inherited in collection.Headers.Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key)))
                if (!headers.Any(h => string.Equals(h.Key.Trim(), inherited.Key.Trim(), StringComparison.OrdinalIgnoreCase)))
                    headers.Add(inherited);
        return headers;
    }

    /// <summary>Auth efectiva: Inherit usa la de la colección; None no envía nada; el resto es propia.</summary>
    public static AuthConfig EffectiveAuth(RequestItem req, RequestCollection? collection) =>
        req.Auth.Type == AuthType.Inherit ? collection?.Auth ?? req.Auth : req.Auth;

    public static async Task<ResponseResult> ExecuteAsync(RequestItem req, RequestCollection? collection,
        EnvironmentModel? env, CancellationToken ct = default)
    {
        var result = new ResponseResult();
        var sw = Stopwatch.StartNew();
        var logUrl = req.Url;
        var logRequestHeaders = "";
        var logRequestBody = "";
        try
        {
            string R(string? s) => VariableResolver.Resolve(s, env);

            var headers = EffectiveHeaders(req, collection);
            var auth = EffectiveAuth(req, collection);
            var method = req.Method;
            var rawUrl = req.Url;
            var rawBody = req.Body.Raw;

            // Pre-request script: puede tocar método, URL, headers, body y variables del ambiente
            if (!string.IsNullOrWhiteSpace(req.PreRequestScript))
            {
                var proxy = new ScriptRequestProxy(method, rawUrl, rawBody,
                    headers.Select(h => (h.Key, h.Value)));
                var pre = ScriptRunner.Run(req.PreRequestScript, env, proxy, null);
                result.ScriptLog = pre.Log.ToString();
                if (pre.Error != null)
                    throw new InvalidOperationException("Error en el pre-request script: " + pre.Error);
                method = proxy.method;
                rawUrl = proxy.url;
                rawBody = proxy.body;
                headers = proxy.ToHeaderList();
            }

            var url = R(rawUrl).Trim();
            logUrl = url;
            if (url.Length == 0) throw new InvalidOperationException("La URL está vacía.");
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "http://" + url;

            if (auth.Type == AuthType.ApiKey && auth.ApiKeyIn == "query" &&
                !string.IsNullOrWhiteSpace(auth.ApiKeyName))
            {
                var sep = url.Contains('?') ? "&" : "?";
                url += sep + Uri.EscapeDataString(R(auth.ApiKeyName)) + "=" + Uri.EscapeDataString(R(auth.ApiKeyValue));
                logUrl = url;
            }

            using var msg = new HttpRequestMessage(new HttpMethod(method), url);

            // Body
            var explicitContentType = headers.FirstOrDefault(h =>
                string.Equals(h.Key?.Trim(), "Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;

            switch (req.Body.Type)
            {
                case BodyType.Json:
                case BodyType.Text:
                    logRequestBody = R(rawBody);
                    msg.Content = new StringContent(logRequestBody, Encoding.UTF8);
                    msg.Content.Headers.Remove("Content-Type");
                    msg.Content.Headers.TryAddWithoutValidation("Content-Type",
                        !string.IsNullOrWhiteSpace(explicitContentType)
                            ? R(explicitContentType)
                            : req.Body.Type == BodyType.Json ? "application/json; charset=utf-8" : "text/plain; charset=utf-8");
                    break;
                case BodyType.FormUrlEncoded:
                    var formPairs = req.Body.FormItems
                        .Where(i => i.Enabled && !string.IsNullOrWhiteSpace(i.Key))
                        .Select(i => new KeyValuePair<string, string>(R(i.Key), R(i.Value)))
                        .ToList();
                    logRequestBody = string.Join("&", formPairs.Select(p => $"{p.Key}={p.Value}"));
                    msg.Content = new FormUrlEncodedContent(formPairs);
                    if (!string.IsNullOrWhiteSpace(explicitContentType))
                    {
                        msg.Content.Headers.Remove("Content-Type");
                        msg.Content.Headers.TryAddWithoutValidation("Content-Type", R(explicitContentType));
                    }
                    break;
            }

            // Headers custom (Content-Type ya fue aplicado al content)
            foreach (var h in headers)
            {
                var key = h.Key.Trim();
                if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                if (!msg.Headers.TryAddWithoutValidation(key, R(h.Value)))
                    msg.Content?.Headers.TryAddWithoutValidation(key, R(h.Value));
            }

            // Auth
            switch (auth.Type)
            {
                case AuthType.Bearer:
                    msg.Headers.TryAddWithoutValidation("Authorization", "Bearer " + R(auth.BearerToken));
                    break;
                case AuthType.Basic:
                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{R(auth.Username)}:{R(auth.Password)}"));
                    msg.Headers.TryAddWithoutValidation("Authorization", "Basic " + credentials);
                    break;
                case AuthType.ApiKey when auth.ApiKeyIn != "query" && !string.IsNullOrWhiteSpace(auth.ApiKeyName):
                    msg.Headers.TryAddWithoutValidation(R(auth.ApiKeyName), R(auth.ApiKeyValue));
                    break;
            }

            // Headers realmente enviados (incluye auth y heredados), para el log
            var sentHeaders = new StringBuilder();
            foreach (var h in msg.Headers)
                sentHeaders.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
            if (msg.Content != null)
                foreach (var h in msg.Content.Headers)
                    sentHeaders.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
            logRequestHeaders = sentHeaders.ToString();

            using var resp = await Client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            sw.Stop();

            result.StatusCode = (int)resp.StatusCode;
            result.StatusText = resp.ReasonPhrase ?? resp.StatusCode.ToString();
            result.IsSuccess = resp.IsSuccessStatusCode;
            result.SizeBytes = bytes.Length;
            result.Body = Encoding.UTF8.GetString(bytes);
            result.ContentType = resp.Content.Headers.ContentType?.MediaType;

            var sb = new StringBuilder();
            foreach (var h in resp.Headers.Concat(resp.Content.Headers))
            {
                var value = string.Join(", ", h.Value);
                sb.AppendLine($"{h.Key}: {value}");
                result.HeaderList.Add(new HeaderEntry { Name = h.Key, Value = value });
            }
            result.HeadersText = sb.ToString();

            // Post-response script: asserts (er.test) y extracción de variables
            if (!string.IsNullOrWhiteSpace(req.TestScript))
            {
                var responseInfo = new ScriptResponseInfo(result.StatusCode, result.Body,
                    result.HeaderList, sw.ElapsedMilliseconds);
                var post = ScriptRunner.Run(req.TestScript, env, null, responseInfo);
                result.ScriptTests = post.Tests;
                result.ScriptError = post.Error;
                var postLog = post.Log.ToString();
                if (postLog.Length > 0)
                    result.ScriptLog = result.ScriptLog.Length > 0 ? result.ScriptLog + "\n" + postLog : postLog;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            result.Error = "Timeout: la request superó el tiempo máximo de espera.";
        }
        catch (Exception ex)
        {
            result.Error = ex.InnerException?.Message ?? ex.Message;
        }
        finally
        {
            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            ExecutionLog.Record(req.Method, logUrl, result, logRequestHeaders, logRequestBody);
        }
        return result;
    }
}
