using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Button = Avalonia.Controls.Button;
using Orientation = Avalonia.Layout.Orientation;

namespace EasyRest.Android;

/// <summary>Pedirle algo a la persona sin abrir una ventana.
///
/// En móvil Avalonia corre sobre una sola actividad y no hay ventanas: un diálogo es una capa
/// encima de la app. <see cref="Instalar"/> la registra una vez —lo hace ShellView al armarse— y
/// desde cualquier vista se llama a Texto, Confirmar u Opciones.
///
/// Es estado estático, que no es lindo, pero acá la alternativa era pasarle el shell a mano a
/// cada control que alguna vez tenga que preguntar algo. La app tiene una sola vista raíz por
/// definición del framework en móvil, así que "el host" es único de verdad.</summary>
internal static class Dialogo
{
    static Panel? _capa;

    internal static void Instalar(Panel capa) => _capa = capa;

    /// <summary>Pide un texto. No llama a alAceptar si queda vacío: todos los usos de acá
    /// (nombres de colección, de carpeta, de request) necesitan algo escrito.</summary>
    public static void Texto(string título, string valorInicial, string marca, Action<string> alAceptar)
    {
        var campo = Ui.Campo(valorInicial, marca);
        Mostrar(título, campo, ("Aceptar", () =>
        {
            var texto = (campo.Text ?? "").Trim();
            if (texto.Length > 0) alAceptar(texto);
        }
        ));
    }

    /// <summary>Un formulario armado por quien llama, cuando un solo campo no alcanza —editar una
    /// variable de ambiente son clave, valor y si es secreta—. El diálogo pone el marco y los dos
    /// botones; el contenido lo trae la pantalla, que es la que sabe qué está editando.</summary>
    public static void Formulario(string título, Control contenido, string aceptar, Action alAceptar) =>
        Mostrar(título, contenido, (aceptar, alAceptar));

    public static void Confirmar(string título, string detalle, string aceptar, Action alAceptar) =>
        Mostrar(título, Ui.Parrafo(detalle, Ui.Subtexto, 13), (aceptar, alAceptar));

    /// <summary>Hoja de acciones: una pila de filas, una por opción. Es el menú contextual del
    /// escritorio traducido a algo que se pueda tocar — filas del alto de un dedo, no un menú de
    /// ítems de 20 px.</summary>
    public static void Opciones(string título, params (string Texto, Action Al)[] opciones)
    {
        var pila = new StackPanel();
        foreach (var (texto, al) in opciones)
        {
            var elegida = al;
            var fila = new Button
            {
                Content = new TextBlock
                {
                    Text = texto,
                    FontSize = 15,
                    Foreground = Ui.Normal,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Background = Brushes.Transparent,
                MinHeight = 52,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            fila.Click += (_, _) => { Cerrar(); elegida(); };
            pila.Children.Add(fila);
        }
        Mostrar(título, pila);
    }

    static void Mostrar(string título, Control contenido, (string Texto, Action Al)? aceptar = null)
    {
        if (_capa == null) return;

        var botones = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        botones.Children.Add(Ui.Fantasma("Cancelar", null, Cerrar));
        if (aceptar is { } ok)
            botones.Children.Add(Ui.Acentuado(ok.Texto, () => { Cerrar(); ok.Al(); }));

        var tarjeta = new Border
        {
            Background = Ui.Panel,
            BorderBrush = Ui.Superficie,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            MaxWidth = 480,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 14,
                Children = { Ui.Titulo(título), contenido, botones }
            }
        };

        // el velo también es el "tocar afuera para cerrar": en un teléfono es el gesto esperado
        var velo = new Button
        {
            Background = new SolidColorBrush(Color.Parse("#000000"), 0.62),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(0)
        };
        velo.Click += (_, _) => Cerrar();

        _capa.Children.Clear();
        _capa.Children.Add(velo);
        _capa.Children.Add(tarjeta);
        _capa.IsVisible = true;
    }

    static void Cerrar()
    {
        if (_capa == null) return;
        _capa.Children.Clear();
        _capa.IsVisible = false;
    }
}
