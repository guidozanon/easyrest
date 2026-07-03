using System.Text.Json;
using System.Windows.Media;

namespace EasyRest;

public class JsonTreeNode
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public Brush ValueBrush { get; set; } = JsonTree.ContainerBrush;
    public bool IsExpanded { get; set; }
    public List<JsonTreeNode> Children { get; } = new();
}

/// <summary>Convierte un body JSON en un árbol navegable de nodos.</summary>
public static class JsonTree
{
    const int MaxNodes = 4000;
    const int MaxValueLength = 300;

    public static readonly Brush StringBrush = Frozen(0xA6, 0xE3, 0xA1);
    public static readonly Brush NumberBrush = Frozen(0xFA, 0xB3, 0x87);
    public static readonly Brush BoolBrush = Frozen(0xCB, 0xA6, 0xF7);
    public static readonly Brush ContainerBrush = Frozen(0x6C, 0x70, 0x86);

    static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Devuelve el árbol del JSON, o null si el texto no es JSON válido.</summary>
    public static List<JsonTreeNode>? TryBuild(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var budget = MaxNodes;
            return new List<JsonTreeNode> { Build("$", doc.RootElement, 0, ref budget) };
        }
        catch
        {
            return null;
        }
    }

    static JsonTreeNode Build(string key, JsonElement element, int depth, ref int budget)
    {
        var node = new JsonTreeNode { Key = key, IsExpanded = depth < 2 };

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (--budget <= 0)
                    {
                        node.Children.Add(new JsonTreeNode { Key = "…", Value = "(truncado)" });
                        break;
                    }
                    node.Children.Add(Build(prop.Name, prop.Value, depth + 1, ref budget));
                }
                node.Value = $"{{{node.Children.Count}}}";
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (--budget <= 0)
                    {
                        node.Children.Add(new JsonTreeNode { Key = "…", Value = "(truncado)" });
                        break;
                    }
                    node.Children.Add(Build($"[{index++}]", item, depth + 1, ref budget));
                }
                node.Value = $"[{node.Children.Count}]";
                break;

            case JsonValueKind.String:
                var s = element.GetString() ?? "";
                if (s.Length > MaxValueLength) s = s[..MaxValueLength] + "…";
                node.Value = $"\"{s}\"";
                node.ValueBrush = StringBrush;
                break;

            case JsonValueKind.Number:
                node.Value = element.GetRawText();
                node.ValueBrush = NumberBrush;
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                node.Value = element.GetRawText();
                node.ValueBrush = BoolBrush;
                break;

            default:
                node.Value = "null";
                break;
        }
        return node;
    }
}
