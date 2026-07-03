using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest;

/// <summary>Hijos de colección/carpeta para el árbol (carpetas primero, después requests).
/// Los CollectionContainer envuelven las ObservableCollection reales: el árbol se actualiza en vivo.
/// Vive en el head WPF porque CompositeCollection es de WPF; el Core no sabe de UI.</summary>
public class TreeChildrenConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        RequestCollection col => new CompositeCollection
        {
            new CollectionContainer { Collection = col.Folders },
            new CollectionContainer { Collection = col.Requests }
        },
        Folder folder => new CompositeCollection
        {
            new CollectionContainer { Collection = folder.Folders },
            new CollectionContainer { Collection = folder.Requests }
        },
        _ => null
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

static class ThemeBrushes
{
    public static readonly Brush Green = Frozen(0xA6, 0xE3, 0xA1);
    public static readonly Brush Red = Frozen(0xF3, 0x8B, 0xA8);
    public static readonly Brush Gray = Frozen(0x6C, 0x70, 0x86);
    public static readonly Brush Peach = Frozen(0xFA, 0xB3, 0x87);
    public static readonly Brush Mauve = Frozen(0xCB, 0xA6, 0xF7);

    static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public class LogKindToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        LogKind.Success => ThemeBrushes.Green,
        LogKind.Failure => ThemeBrushes.Red,
        _ => ThemeBrushes.Gray
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class PassedToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "✔" : "✖";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class PassedToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? ThemeBrushes.Green : ThemeBrushes.Red;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class JsonKindToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        JsonNodeKind.String => ThemeBrushes.Green,
        JsonNodeKind.Number => ThemeBrushes.Peach,
        JsonNodeKind.Bool => ThemeBrushes.Mauve,
        _ => ThemeBrushes.Gray
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
