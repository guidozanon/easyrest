using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EasyRest.Models;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace EasyRest.Services;

public static class OpenApiImporter
{
    /// <summary>Importa un documento OpenAPI (JSON o YAML) y genera una colección con una request por operación.</summary>
    public static (RequestCollection Collection, string? BaseUrl) Import(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var doc = new OpenApiStreamReader().Read(stream, out var diagnostic);

        if (doc?.Paths == null || doc.Paths.Count == 0)
        {
            var detail = diagnostic?.Errors is { Count: > 0 } errors
                ? string.Join("; ", errors.Select(e => e.Message))
                : "el documento no contiene paths";
            throw new InvalidOperationException("Documento OpenAPI inválido: " + detail);
        }

        var baseUrl = doc.Servers?.FirstOrDefault()?.Url?.TrimEnd('/');
        var collection = new RequestCollection
        {
            Name = string.IsNullOrWhiteSpace(doc.Info?.Title)
                ? Path.GetFileNameWithoutExtension(filePath)
                : doc.Info!.Title.Trim()
        };

        foreach (var (path, pathItem) in doc.Paths.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (var (opType, op) in pathItem.Operations)
            {
                var method = opType.ToString().ToUpperInvariant();
                // {param} de OpenAPI -> {{param}} (sintaxis de variables de EasyRest)
                var url = "{{baseUrl}}" + Regex.Replace(path, @"\{([^}]+)\}", "{{$1}}");

                var parameters = (pathItem.Parameters ?? Enumerable.Empty<OpenApiParameter>())
                    .Concat(op.Parameters ?? Enumerable.Empty<OpenApiParameter>())
                    .ToList();

                // Query params: los requeridos van habilitados (y a la URL), los opcionales quedan
                // deshabilitados en la solapa Params para activarlos a mano.
                var queryParams = parameters.Where(p => p.In == ParameterLocation.Query).ToList();
                var enabledQuery = queryParams.Where(p => p.Required).ToList();
                if (enabledQuery.Count > 0)
                    url += "?" + string.Join("&", enabledQuery.Select(p => $"{p.Name}={{{{{p.Name}}}}}"));

                var request = new RequestItem
                {
                    Name = string.IsNullOrWhiteSpace(op.Summary) ? $"{method} {path}" : op.Summary!.Trim(),
                    Method = method,
                    Url = url,
                    Description = op.Description?.Trim() ?? ""
                };

                foreach (var qp in queryParams)
                    request.QueryParams.Add(new KeyValueItem
                    {
                        Enabled = qp.Required,
                        Key = qp.Name,
                        Value = $"{{{{{qp.Name}}}}}"
                    });

                foreach (var hp in parameters.Where(p => p.In == ParameterLocation.Header))
                    request.Headers.Add(new KeyValueItem
                    {
                        Enabled = hp.Required,
                        Key = hp.Name,
                        Value = $"{{{{{hp.Name}}}}}"
                    });

                var jsonContent = op.RequestBody?.Content?
                    .FirstOrDefault(c => c.Key.Contains("json", StringComparison.OrdinalIgnoreCase)).Value;
                if (jsonContent?.Schema != null)
                {
                    request.Body.Type = BodyType.Json;
                    request.Body.Raw = (Sample(jsonContent.Schema, 0) ?? new JsonObject())
                        .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                }

                // Agrupado: si la operación tiene tags, va a una carpeta con el primer tag.
                // Si no, se anida por los segmentos del path (sin parámetros y sin el último
                // segmento, que pertenece a la request): /odata/bookings -> carpeta "odata".
                var tag = op.Tags?.FirstOrDefault()?.Name?.Trim();
                string[] folderPath;
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    folderPath = new[] { tag };
                }
                else
                {
                    var segments = FolderSegments(path);
                    folderPath = segments.Length > 1 ? segments[..^1] : Array.Empty<string>();
                }

                if (folderPath.Length == 0)
                    collection.Requests.Add(request);
                else
                    GetOrCreateFolder(collection, folderPath).Requests.Add(request);
            }
        }

        return (collection, baseUrl);
    }

    static string[] FolderSegments(string path) =>
        path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !s.StartsWith('{'))
            .ToArray();

    static Folder GetOrCreateFolder(RequestCollection collection, string[] segments)
    {
        var current = collection.Folders;
        Folder folder = null!;
        foreach (var segment in segments)
        {
            var existing = current.FirstOrDefault(f =>
                string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new Folder { Name = segment };
                current.Add(existing);
            }
            folder = existing;
            current = existing.Folders;
        }
        return folder;
    }

    static JsonNode? Sample(OpenApiSchema? schema, int depth)
    {
        if (schema == null || depth > 4) return null;

        if (schema.AllOf is { Count: > 0 })
        {
            var merged = new JsonObject();
            foreach (var sub in schema.AllOf)
            {
                if (Sample(sub, depth + 1) is not JsonObject obj) continue;
                foreach (var kv in obj.ToList())
                {
                    obj.Remove(kv.Key);
                    merged[kv.Key] = kv.Value;
                }
            }
            if (merged.Count > 0) return merged;
        }

        switch (schema.Type)
        {
            case "object":
            case null:
                if (schema.Properties is { Count: > 0 })
                {
                    var obj = new JsonObject();
                    foreach (var (name, prop) in schema.Properties)
                        obj[name] = Sample(prop, depth + 1);
                    return obj;
                }
                return schema.Type == "object" ? new JsonObject() : null;
            case "array":
                var arr = new JsonArray();
                if (Sample(schema.Items, depth + 1) is { } item) arr.Add(item);
                return arr;
            case "integer":
                return 0;
            case "number":
                return 0.0;
            case "boolean":
                return true;
            case "string":
                if (schema.Enum is { Count: > 0 } && schema.Enum[0] is OpenApiString enumValue)
                    return enumValue.Value;
                return schema.Format switch
                {
                    "date-time" => "2026-01-01T00:00:00Z",
                    "date" => "2026-01-01",
                    "uuid" => "00000000-0000-0000-0000-000000000000",
                    "email" => "user@example.com",
                    "uri" => "https://example.com",
                    _ => "string"
                };
            default:
                return null;
        }
    }
}
