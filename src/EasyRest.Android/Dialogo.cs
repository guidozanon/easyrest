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

    public static void Confirmar(string título, string detalle, string aceptar, Action alAceptar) =>
        Mostrar(título, Ui.Parrafo(detalle, Ui.Tenue, 12), (aceptar, alAceptar));

    /// <summary>Hoja de acciones: una pila de botones, uno por opción. Es el menú contextual del
    /// escritorio traducido a algo que se pueda tocar.</summary>
    public static void Opciones(string título, params (string Texto, Action Al)[] opciones)
    {
        var pila = new StackPanel { Spacing = 6 };
        foreach (var (texto, al) in opciones)
        {
            var elegida = al;
            var boton = Ui.Accion(texto, () => { Cerrar(); elegida(); });
            boton.HorizontalAlignment = HorizontalAlignment.Stretch;
            boton.HorizontalContentAlignment = HorizontalAlignment.Left;
            pila.Children.Add(boton);
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
        botones.Children.Add(Ui.Accion("Cancelar", Cerrar));
        if (aceptar is { } ok)
        {
            var boton = Ui.Accion(ok.Texto, () => { Cerrar(); ok.Al(); });
            boton.Background = Ui.Acento;
            boton.Foreground = Ui.Fondo;
            botones.Children.Add(boton);
        }

        var tarjeta = new Border
        {
            Background = Ui.Panel,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            MaxWidth = 480,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 12,
                Children = { Ui.Titulo(título), contenido, botones }
            }
        };

        // el velo también es el "tocar afuera para cerrar": en un teléfono es el gesto esperado
        var velo = new Button
        {
            Background = new SolidColorBrush(Color.Parse("#B0000000")),
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
