using System.Text.Json;
using EasyRest.Models;

namespace EasyRest.Services;

/// <summary>Export/import de ambientes para compartirlos (portapapeles o archivo).
/// Los ambientes nunca van al repo, así que compartirlos es siempre un acto explícito.
/// Acepta el formato propio y el export de environment de Postman.</summary>
public static class EnvironmentShare
{
    // camelCase para que el formato compartido siga siendo exactamente el mismo de antes
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>El JSON que se comparte. Son clases y no tipos anónimos a propósito: un tipo
    /// anónimo no se puede serializar en un runtime AOT (System.Text.Json no puede construirle
    /// el converter), y eso rompería la app en móvil. Verificado con tests/aot-probe.</summary>
    sealed class SharedEnvironment
    {
        public string Easyrest { get; init; } = "environment";
        public string Name { get; init; } = "";
        public List<SharedVariable> Variables { get; init; } = new();
    }

    sealed class SharedVariable
    {
        public string Key { get; init; } = "";
        public string Value { get; init; } = "";
        public bool Enabled { get; init; }
    }

    /// <summary>Serializa el ambiente a un JSON compartible. Con includeValues=false van
    /// solo las claves, para pasar la estructura sin filtrar tokens ni secretos.</summary>
    public static string ToJson(EnvironmentModel env, bool includeValues = true)
    {
        var payload = new SharedEnvironment
        {
            Name = env.Name,
            Variables = env.Variables.Select(v => new SharedVariable
            {
                Key = v.Key,
                Value = includeValues ? v.Value : "",
                Enabled = v.Enabled
            }).ToList()
        };
        return JsonSerializer.Serialize(payload, Options);
    }

    /// <summary>Intenta interpretar el texto como un ambiente compartido: el formato propio
    /// ("variables") o un environment de Postman ("values"). Devuelve un ambiente nuevo con
    /// Id propio, o null si el texto no es un ambiente.</summary>
    public static EnvironmentModel? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var items = Get(root, "variables") ?? Get(root, "values");
            if (items is not { ValueKind: JsonValueKind.Array } vars) return null;

            var name = AsString(Get(root, "name")).Trim();
            var env = new EnvironmentModel { Name = name.Length > 0 ? name : "Ambiente importado" };
            foreach (var item in vars.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var key = AsString(Get(item, "key")).Trim();
                if (key.Length == 0) continue;
                env.Variables.Add(new KeyValueItem
                {
                    Key = key,
                    Value = AsString(Get(item, "value")),
                    Enabled = Get(item, "enabled")?.ValueKind != JsonValueKind.False
                });
            }
            return env;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static JsonElement? Get(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        return null;
    }

    static string AsString(JsonElement? element) => element is { } e
        ? e.ValueKind switch
        {
            JsonValueKind.String => e.GetString() ?? "",
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => e.GetRawText()
        }
        : "";
}
