using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Button = Avalonia.Controls.Button;

namespace EasyRest.Android;

/// <summary>Las piezas visuales que comparten todas las pantallas del head: paleta, botones,
/// campos y tarjetas.
///
/// Vive aparte de las vistas porque ahora hay varias y todas necesitan lo mismo. Sigue siendo C#
/// y no XAML por el bug de build que documenta docs/ANDROID.md.
///
/// La regla de oro acá es el dedo: <see cref="Toque"/> es la altura mínima de cualquier cosa que
/// se pueda tocar. Android pide 48dp y Avalonia mide en las mismas unidades independientes de
/// densidad, así que el número es directamente ese.</summary>
internal static class Ui
{
    public const double Toque = 48;

    public static readonly IBrush Fondo = new SolidColorBrush(Color.Parse("#1E1E2E"));
    public static readonly IBrush Panel = new SolidColorBrush(Color.Parse("#272739"));
    public static readonly IBrush PanelAlto = new SolidColorBrush(Color.Parse("#313244"));
    public static readonly IBrush Acento = new SolidColorBrush(Color.Parse("#89B4FA"));
    public static readonly IBrush Tenue = new SolidColorBrush(Color.Parse("#9399B2"));
    public static readonly IBrush Normal = new SolidColorBrush(Color.Parse("#CDD6F4"));
    public static readonly IBrush Verde = new SolidColorBrush(Color.Parse("#A6E3A1"));
    public static readonly IBrush Rojo = new SolidColorBrush(Color.Parse("#F38BA8"));
    public static readonly IBrush Amarillo = new SolidColorBrush(Color.Parse("#F9E2AF"));

    public static Button Accion(string texto, Action al)
    {
        var boton = Boton(texto);
        boton.Click += (_, _) => al();
        return boton;
    }

    /// <summary>Separada de la sincrónica y no una sobrecarga: un lambda como `() => AlgoAsync()`
    /// encaja en Action y en Func&lt;Task&gt;, y el compilador no sabe cuál querés.</summary>
    public static Button AccionAsync(string texto, Func<Task> al)
    {
        var boton = Boton(texto);
        boton.Click += async (_, _) => await al();
        return boton;
    }

    static Button Boton(string texto) => new()
    {
        Content = texto,
        Padding = new Thickness(14, 10),
        MinHeight = Toque,
        FontSize = 13
    };

    /// <summary>Botón de una fila de opciones excluyentes (método, solapa, tipo de cuerpo). El
    /// activo se pinta lleno: en un teléfono no hay hover que ayude a saber dónde estás.</summary>
    public static Button Opcion(string texto, bool activo, Action al)
    {
        var boton = new Button
        {
            Content = texto,
            Padding = new Thickness(12, 8),
            MinHeight = 40,
            FontSize = 13,
            Background = activo ? Acento : PanelAlto,
            Foreground = activo ? Fondo : Normal
        };
        boton.Click += (_, _) => al();
        return boton;
    }

    public static TextBlock Parrafo(string texto, IBrush? color = null, double tamaño = 13) => new()
    {
        Text = texto,
        FontSize = tamaño,
        Foreground = color ?? Normal,
        TextWrapping = TextWrapping.Wrap
    };

    public static TextBlock Rotulo(string texto) => new()
    {
        Text = texto,
        FontSize = 11,
        Foreground = Tenue
    };

    public static TextBlock Titulo(string texto) => new()
    {
        Text = texto,
        FontSize = 14,
        FontWeight = FontWeight.SemiBold,
        Foreground = Acento
    };

    public static TextBox Campo(string texto, string? marca = null, bool multilinea = false,
        bool mono = false) => new()
    {
        Text = texto,
        Watermark = marca,
        AcceptsReturn = multilinea,
        TextWrapping = multilinea ? TextWrapping.Wrap : TextWrapping.NoWrap,
        MinHeight = multilinea ? 120 : Toque,
        FontSize = mono ? 12 : 14,
        FontFamily = mono ? "monospace" : FontFamily.Default
    };

    public static Border Tarjeta(params Control[] hijos)
    {
        var pila = new StackPanel { Spacing = 8 };
        foreach (var hijo in hijos) pila.Children.Add(hijo);
        return new Border
        {
            Background = Panel,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = pila
        };
    }

    /// <summary>Fila de botones que baja de renglón sola. En un teléfono angosto una barra de
    /// acciones en StackPanel horizontal se corta; acá se acomoda.</summary>
    public static WrapPanel Barra(params Control[] hijos)
    {
        var barra = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var hijo in hijos)
        {
            hijo.Margin = new Thickness(0, 0, 8, 8);
            barra.Children.Add(hijo);
        }
        return barra;
    }
}
