using System.Text.Json;
using System.Text.Json.Nodes;

namespace EasyRest.Services.Sync;

/// <summary>Separa y vuelve a juntar los valores secretos de un ambiente.
///
/// En disco el ambiente tiene los valores completos, porque la app los usa. Al server viaja
/// partido: el documento sin los valores marcados como secretos, y esos valores aparte, que el
/// server cifra y sólo devuelve a quien tiene permiso. La lista de claves secretas va en
/// "secretKeys" dentro del propio ambiente.</summary>
public static class EnvironmentDocument
{
    const string SecretKeysProperty = "secretKeys";
    const string VariablesProperty = "variables";

    /// <summary>Todo lo que esté bajo esta carpeta del workspace se trata como ambiente.</summary>
    public const string FolderName = "environments";

    /// <summary>Parte el JSON del ambiente en (documento sin secretos, valores secretos).</summary>
    public static (string Content, Dictionary<string, string> Secrets) Split(string json)
    {
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);

        var root = Parse(json);
        if (root == null) return (json, secrets);

        var secretKeys = ReadSecretKeys(root);
        if (secretKeys.Count == 0) return (json, secrets);

        if (root[VariablesProperty] is not JsonArray variables) return (json, secrets);

        foreach (var variable in variables)
        {
            if (variable is not JsonObject obj) continue;
            var key = obj["key"]?.GetValue<string>();
            if (key == null || !secretKeys.Contains(key)) continue;

            var value = obj["value"]?.GetValue<string>() ?? "";
            if (value.Length > 0) secrets[key] = value;
            obj["value"] = "";
        }

        return (root.ToJsonString(Options), secrets);
    }

    /// <summary>Vuelve a poner los valores secretos dentro del ambiente para guardarlo en disco.
    /// Las claves que no vinieron (porque no hay permiso) quedan vacías, que es exactamente lo
    /// que ya hacía el "compartir sólo las claves".</summary>
    public static string Merge(string content, IReadOnlyDictionary<string, string>? secrets)
    {
        if (secrets == null || secrets.Count == 0) return content;

        var root = Parse(content);
        if (root?[VariablesProperty] is not JsonArray variables) return content;

        foreach (var variable in variables)
        {
            if (variable is not JsonObject obj) continue;
            var key = obj["key"]?.GetValue<string>();
            if (key != null && secrets.TryGetValue(key, out var value)) obj["value"] = value;
        }

        return root.ToJsonString(Options);
    }

    /// <summary>Un ambiente tiene secretos declarados: sólo esos justifican pedirle al server el
    /// endpoint aparte.</summary>
    public static bool HasSecrets(string json) => ReadSecretKeys(Parse(json)).Count > 0;

    static HashSet<string> ReadSecretKeys(JsonObject? root)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (root?[SecretKeysProperty] is not JsonArray array) return keys;

        foreach (var item in array)
            if (item?.GetValue<string>() is { Length: > 0 } key) keys.Add(key);
        return keys;
    }

    static JsonObject? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
}
