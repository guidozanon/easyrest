using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Data.Converters;
using Avalonia.Media;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest.Avalonia;

static class Palette
{
    public static readonly IBrush Green = Brush.Parse("#A6E3A1");
    public static readonly IBrush Red = Brush.Parse("#F38BA8");
    public static readonly IBrush Gray = Brush.Parse("#6C7086");
    public static readonly IBrush Peach = Brush.Parse("#FAB387");
    public static readonly IBrush Mauve = Brush.Parse("#CBA6F7");
    public static readonly IBrush Blue = Brush.Parse("#89B4FA");
    public static readonly IBrush Text = Brush.Parse("#CDD6F4");
    public static readonly IBrush Surface1 = Brush.Parse("#45475A");
    public static readonly IBrush Hover = Brush.Parse("#2A2A3E");
}

/// <summary>Fondo del highlight de una fila del árbol: [0]=IsSelected, [1]=IsPointerOver.
/// Se resuelve contra la propia fila para que seleccionar una carpeta no marque sus hijos.</summary>
public class RowHighlightConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = values.Count > 0 && values[0] is true;
        var hover = values.Count > 1 && values[1] is true;
        if (selected) return Palette.Surface1;
        if (hover) return Palette.Hover;
        return Brushes.Transparent;
    }
}

/// <summary>Color por método HTTP.</summary>
public class MethodToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString()?.ToUpperInvariant() switch
        {
            "GET" => Palette.Green,
            "POST" => Palette.Peach,
            "PUT" => Palette.Blue,
            "PATCH" => Palette.Mauve,
            "DELETE" => Palette.Red,
            _ => Palette.Gray
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class LogKindToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        LogKind.Success => Palette.Green,
        LogKind.Failure => Palette.Red,
        _ => Palette.Gray
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class JsonKindToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        JsonNodeKind.String => Palette.Green,
        JsonNodeKind.Number => Palette.Peach,
        JsonNodeKind.Bool => Palette.Mauve,
        _ => Palette.Gray
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class PassedToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "✔" : "✖";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class PassedToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Palette.Green : Palette.Red;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

public class AuthTypeToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        AuthType.Inherit => "Heredar de la colección",
        AuthType.None => "None (sin autenticación)",
        AuthType.Bearer => "Bearer Token",
        AuthType.Basic => "Basic Auth",
        AuthType.ApiKey => "API Key",
        _ => value?.ToString() ?? ""
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

/// <summary>true si el objeto es del tipo nombrado en el parámetro (para discriminar tabs por tipo).</summary>
public class IsTypeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value != null && value.GetType().Name == parameter as string;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

/// <summary>true si el string no está vacío (para IsVisible).</summary>
public class NotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

/// <summary>Hijos de colección/carpeta para el TreeView (carpetas primero, después requests).</summary>
public class TreeChildrenConverter : IValueConverter
{
    // una vista por nodo, reutilizada: así el árbol mantiene el binding y se refresca
    // cuando cambian las colecciones (drag&drop, alta, baja, importación).
    static readonly ConditionalWeakTable<object, ChildrenView> _cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RequestCollection or Folder ? _cache.GetValue(value, k => new ChildrenView(k)) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

/// <summary>Hijos de un nodo del árbol: [carpetas…, requests…] como colección observable.
/// Se re-arma cuando cambian las colecciones de origen para que el árbol se actualice solo.</summary>
public class ChildrenView : ObservableCollection<object>
{
    readonly IEnumerable _folders;
    readonly IEnumerable _requests;

    public ChildrenView(object node)
    {
        (_folders, _requests) = node switch
        {
            RequestCollection col => ((IEnumerable)col.Folders, (IEnumerable)col.Requests),
            Folder f => (f.Folders, f.Requests),
            _ => (Array.Empty<object>(), Array.Empty<object>())
        };
        if (_folders is INotifyCollectionChanged nf) nf.CollectionChanged += (_, _) => Rebuild();
        if (_requests is INotifyCollectionChanged nr) nr.CollectionChanged += (_, _) => Rebuild();
        Rebuild();
    }

    void Rebuild()
    {
        Clear();
        foreach (var f in _folders) Add(f!);
        foreach (var r in _requests) Add(r!);
    }
}
