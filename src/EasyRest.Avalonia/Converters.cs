using System.Collections;
using System.Globalization;
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
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var list = new List<object>();
        switch (value)
        {
            case RequestCollection col:
                list.AddRange(col.Folders);
                list.AddRange(col.Requests);
                break;
            case Folder folder:
                list.AddRange(folder.Folders);
                list.AddRange(folder.Requests);
                break;
        }
        return list;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
