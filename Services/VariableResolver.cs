using System.Text.RegularExpressions;
using EasyRest.Models;

namespace EasyRest.Services;

public static class VariableResolver
{
    static readonly Regex VarRegex = new(@"\{\{\s*([\w.$\-]+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>Reemplaza {{variable}} por su valor en el ambiente activo. Las variables sin definir quedan como están.</summary>
    public static string Resolve(string? input, EnvironmentModel? env)
    {
        if (string.IsNullOrEmpty(input)) return "";
        if (env == null) return input;
        return VarRegex.Replace(input, m =>
        {
            var variable = env.Variables.FirstOrDefault(v =>
                v.Enabled && string.Equals(v.Key?.Trim(), m.Groups[1].Value, StringComparison.Ordinal));
            return variable?.Value ?? m.Value;
        });
    }
}
