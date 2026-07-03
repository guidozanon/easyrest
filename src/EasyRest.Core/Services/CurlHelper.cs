using System.Text;
using EasyRest.Models;

namespace EasyRest.Services;

public class ParsedCurl
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public List<KeyValuePair<string, string>> Headers { get; } = new();
    public string? Body { get; set; }
    public string? BasicUser { get; set; }
    public string? BasicPassword { get; set; }
}

/// <summary>Genera y parsea comandos cURL (formato bash, como exporta Postman).</summary>
public static class CurlHelper
{
    // ----- Generación -----

    public static string ToCurl(RequestItem req, RequestCollection? collection, EnvironmentModel? env)
    {
        string R(string? s) => VariableResolver.Resolve(s, env);

        var sb = new StringBuilder("curl");
        var method = req.Method.ToUpperInvariant();
        if (method != "GET") sb.Append($" -X {method}");

        var auth = HttpExecutor.EffectiveAuth(req, collection);
        var url = R(req.Url).Trim();
        if (auth.Type == AuthType.ApiKey && auth.ApiKeyIn == "query" && !string.IsNullOrWhiteSpace(auth.ApiKeyName))
        {
            var sep = url.Contains('?') ? "&" : "?";
            url += sep + Uri.EscapeDataString(R(auth.ApiKeyName)) + "=" + Uri.EscapeDataString(R(auth.ApiKeyValue));
        }
        sb.Append($" '{Esc(url)}'");

        var headers = HttpExecutor.EffectiveHeaders(req, collection);
        var hasContentType = headers.Any(h =>
            string.Equals(h.Key.Trim(), "Content-Type", StringComparison.OrdinalIgnoreCase));
        foreach (var h in headers)
            sb.Append($" \\\n  -H '{Esc(h.Key.Trim())}: {Esc(R(h.Value))}'");

        switch (auth.Type)
        {
            case AuthType.Bearer:
                sb.Append($" \\\n  -H 'Authorization: Bearer {Esc(R(auth.BearerToken))}'");
                break;
            case AuthType.Basic:
                sb.Append($" \\\n  -u '{Esc(R(auth.Username))}:{Esc(R(auth.Password))}'");
                break;
            case AuthType.ApiKey when auth.ApiKeyIn != "query" && !string.IsNullOrWhiteSpace(auth.ApiKeyName):
                sb.Append($" \\\n  -H '{Esc(R(auth.ApiKeyName))}: {Esc(R(auth.ApiKeyValue))}'");
                break;
        }

        switch (req.Body.Type)
        {
            case BodyType.Json:
                if (!hasContentType) sb.Append(" \\\n  -H 'Content-Type: application/json'");
                sb.Append($" \\\n  -d '{Esc(R(req.Body.Raw))}'");
                break;
            case BodyType.Text:
                if (!hasContentType) sb.Append(" \\\n  -H 'Content-Type: text/plain'");
                sb.Append($" \\\n  -d '{Esc(R(req.Body.Raw))}'");
                break;
            case BodyType.FormUrlEncoded:
                var pairs = req.Body.FormItems
                    .Where(i => i.Enabled && !string.IsNullOrWhiteSpace(i.Key))
                    .Select(i => $"{R(i.Key)}={R(i.Value)}");
                sb.Append($" \\\n  -d '{Esc(string.Join("&", pairs))}'");
                break;
        }

        return sb.ToString();
    }

    static string Esc(string s) => s.Replace("'", "'\\''");

    // ----- Parseo -----

    public static bool TryParse(string text, out ParsedCurl parsed)
    {
        parsed = new ParsedCurl();
        text = text.Trim();
        if (!text.StartsWith("curl", StringComparison.OrdinalIgnoreCase)) return false;

        // "Copy as cURL (cmd)" de DevTools usa ^" como comillas: normalizarlas
        if (text.Contains("^\"")) text = text.Replace("^\"", "\"");

        var tokens = Tokenize(text);
        if (tokens.Count == 0 || !tokens[0].Equals("curl", StringComparison.OrdinalIgnoreCase)) return false;

        string? method = null, url = null, body = null, user = null;

        for (var i = 1; i < tokens.Count; i++)
        {
            var t = tokens[i];
            string? Next() => i + 1 < tokens.Count ? tokens[++i] : null;

            switch (t)
            {
                case "-X" or "--request":
                    method = Next()?.ToUpperInvariant();
                    break;
                case "-H" or "--header":
                    var header = Next();
                    if (header != null)
                    {
                        var colon = header.IndexOf(':');
                        if (colon > 0)
                            parsed.Headers.Add(new KeyValuePair<string, string>(
                                header[..colon].Trim(), header[(colon + 1)..].Trim()));
                    }
                    break;
                case "-d" or "--data" or "--data-raw" or "--data-binary" or "--data-ascii":
                    body = Next();
                    break;
                case "-u" or "--user":
                    user = Next();
                    break;
                case "-b" or "--cookie":
                    var cookie = Next();
                    if (cookie != null) parsed.Headers.Add(new KeyValuePair<string, string>("Cookie", cookie));
                    break;
                case "-A" or "--user-agent":
                    var agent = Next();
                    if (agent != null) parsed.Headers.Add(new KeyValuePair<string, string>("User-Agent", agent));
                    break;
                case "--url":
                    url = Next();
                    break;
                // flags con argumento que ignoramos
                case "-o" or "--output" or "-e" or "--referer" or "-m" or "--max-time" or "--connect-timeout":
                    Next();
                    break;
                default:
                    if (!t.StartsWith('-') && url == null &&
                        (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                        url = t;
                    break;
            }
        }

        if (url == null) return false;

        parsed.Url = url;
        parsed.Body = body;
        parsed.Method = method ?? (body != null ? "POST" : "GET");
        if (user != null)
        {
            var colon = user.IndexOf(':');
            parsed.BasicUser = colon >= 0 ? user[..colon] : user;
            parsed.BasicPassword = colon >= 0 ? user[(colon + 1)..] : "";
        }
        return true;
    }

    /// <summary>Tokeniza respetando comillas simples/dobles y continuaciones de línea (\, ^ y `).</summary>
    static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var inSingle = false;
        var inDouble = false;

        void Flush()
        {
            if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
        }

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (inSingle)
            {
                if (c == '\'') inSingle = false; else sb.Append(c);
            }
            else if (inDouble)
            {
                if (c == '"') inDouble = false;
                else if (c == '\\' && i + 1 < s.Length && (s[i + 1] == '"' || s[i + 1] == '\\')) sb.Append(s[++i]);
                else sb.Append(c);
            }
            else if (c == '\'') inSingle = true;
            else if (c == '"') inDouble = true;
            else if (c is '\\' or '^' or '`' && i + 1 < s.Length && (s[i + 1] == '\n' || s[i + 1] == '\r'))
            {
                // continuación de línea: saltear el salto
                while (i + 1 < s.Length && (s[i + 1] == '\n' || s[i + 1] == '\r')) i++;
            }
            else if (c == '\\' && i + 1 < s.Length)
            {
                // escape estilo bash fuera de comillas: \x = x literal (p. ej. '\'' en bodies)
                sb.Append(s[++i]);
            }
            else if (char.IsWhiteSpace(c)) Flush();
            else sb.Append(c);
        }
        Flush();
        return tokens;
    }
}
