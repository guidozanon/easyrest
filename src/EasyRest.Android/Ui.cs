using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Button = Avalonia.Controls.Button;
using Forma = Avalonia.Controls.Shapes.Path;
using Orientation = Avalonia.Layout.Orientation;

namespace EasyRest.Android;

/// <summary>El sistema visual del head: paleta, tipografía y las piezas que arman las pantallas.
///
/// Es el mismo sistema del escritorio —Catppuccin Mocha, los colores salen de Theme.axaml— con la
/// densidad cambiada: en el escritorio una fila mide 24 px y acá <see cref="Toque"/>, porque lo
/// que apunta es un dedo y no un mouse. Android pide 48dp de mínimo y Avalonia mide en las mismas
/// unidades, así que el número es directamente ese.
///
/// Las pantallas no eligen colores ni tamaños sueltos: piden piezas de acá. Es lo que evita que
/// cada vista invente su propio gris.</summary>
internal static class Ui
{
    public const double Toque = 48;

    // ----- Paleta (Theme.axaml del escritorio) -----

    public static readonly Color CFondo = Color.Parse("#1E1E2E");
    public static readonly Color CPanel = Color.Parse("#181825");
    public static readonly Color CCorteza = Color.Parse("#11111B");
    public static readonly Color CSuperficie = Color.Parse("#313244");
    public static readonly Color CBorde = Color.Parse("#262637");
    public static readonly Color CTexto = Color.Parse("#CDD6F4");
    public static readonly Color CSubtexto = Color.Parse("#A6ADC8");
    public static readonly Color CTenue = Color.Parse("#6C7086");
    public static readonly Color CAcento = Color.Parse("#89B4FA");
    public static readonly Color CVerde = Color.Parse("#A6E3A1");
    public static readonly Color CRojo = Color.Parse("#F38BA8");
    public static readonly Color CDurazno = Color.Parse("#FAB387");
    public static readonly Color CAmarillo = Color.Parse("#F9E2AF");
    public static readonly Color CMalva = Color.Parse("#CBA6F7");

    public static readonly IBrush Fondo = new SolidColorBrush(CFondo);
    public static readonly IBrush Panel = new SolidColorBrush(CPanel);
    public static readonly IBrush Corteza = new SolidColorBrush(CCorteza);
    public static readonly IBrush Superficie = new SolidColorBrush(CSuperficie);
    public static readonly IBrush Borde = new SolidColorBrush(CBorde);
    public static readonly IBrush Normal = new SolidColorBrush(CTexto);
    public static readonly IBrush Subtexto = new SolidColorBrush(CSubtexto);
    public static readonly IBrush Tenue = new SolidColorBrush(CTenue);
    public static readonly IBrush Acento = new SolidColorBrush(CAcento);
    public static readonly IBrush Verde = new SolidColorBrush(CVerde);
    public static readonly IBrush Rojo = new SolidColorBrush(CRojo);
    public static readonly IBrush Durazno = new SolidColorBrush(CDurazno);
    public static readonly IBrush Amarillo = new SolidColorBrush(CAmarillo);
    public static readonly IBrush Malva = new SolidColorBrush(CMalva);

    /// <summary>El mismo color, apenas insinuado: es el fondo de las pastillas y los avisos. Se
    /// usa opacidad y no un gris mezclado a mano para que siga funcionando si cambia el fondo.</summary>
    public static IBrush Tinte(Color color, double opacidad = 0.16) => new SolidColorBrush(color, opacidad);

    /// <summary>El método pinta la request en toda la app. Es la señal que hace que una lista de
    /// doscientas se lea de un vistazo, así que el color vive acá y no en cada vista.</summary>
    public static Color ColorDeMetodo(string metodo) => metodo.ToUpperInvariant() switch
    {
        "POST" => CVerde,
        "PUT" => CDurazno,
        "PATCH" => CAmarillo,
        "DELETE" => CRojo,
        _ => CAcento
    };

    public static Color ColorDeEstado(int codigo) => codigo switch
    {
        >= 200 and < 300 => CVerde,
        >= 300 and < 400 => CAcento,
        >= 400 and < 500 => CDurazno,
        _ => CRojo
    };

    // ----- Texto -----

    /// <summary>Centrado en vertical a propósito: casi siempre va al lado de un botón de 48 px, y
    /// un TextBlock sin alineación se estira y pinta el texto arriba de todo — que es lo que hacía
    /// que el título quedara más alto que la flecha de volver.</summary>
    public static TextBlock Titulo(string texto) => new()
    {
        Text = texto,
        FontSize = 17,
        FontWeight = FontWeight.SemiBold,
        Foreground = Normal,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static TextBlock Parrafo(string texto, IBrush? color = null, double tamaño = 13) => new()
    {
        Text = texto,
        FontSize = tamaño,
        Foreground = color ?? Normal,
        TextWrapping = TextWrapping.Wrap
    };

    public static TextBlock Rotulo(string texto) => new()
    {
        Text = texto.ToUpperInvariant(),
        FontSize = 11,
        FontWeight = FontWeight.Bold,
        Foreground = Tenue
    };

    public static TextBlock Nota(string texto) => new()
    {
        Text = texto,
        FontSize = 11,
        Foreground = Tenue,
        TextWrapping = TextWrapping.Wrap
    };

    public static TextBlock Mono(string texto, IBrush? color = null, double tamaño = 12) => new()
    {
        Text = texto,
        FontSize = tamaño,
        FontFamily = "Consolas,Menlo,monospace",
        Foreground = color ?? Normal,
        TextWrapping = TextWrapping.Wrap
    };

    // ----- Íconos -----

    public static Forma Icono(Geometry geometria, double tamaño, IBrush color, bool relleno = false) => new()
    {
        Data = geometria,
        Width = tamaño,
        Height = tamaño,
        Stretch = Stretch.Uniform,
        Fill = relleno ? color : null,
        Stroke = relleno ? null : color,
        StrokeThickness = 1.8,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        VerticalAlignment = VerticalAlignment.Center
    };

    // ----- Botones -----

    /// <summary>La acción de la pantalla: una sola, llena y con el color de acento.</summary>
    public static Button Primario(string texto, Geometry? icono, Action al)
    {
        var boton = Base(texto, icono, Corteza, 16, FontWeight.SemiBold);
        boton.Background = Acento;
        boton.MinHeight = 52;
        boton.CornerRadius = new CornerRadius(12);
        boton.HorizontalAlignment = HorizontalAlignment.Stretch;
        boton.HorizontalContentAlignment = HorizontalAlignment.Center;
        boton.Click += (_, _) => al();
        return boton;
    }

    /// <summary>Como el primario pero del alto de los demás: para el «Aceptar» de un diálogo,
    /// donde un botón de 52 px al lado de un «Cancelar» de 48 queda torcido.</summary>
    public static Button Acentuado(string texto, Action al)
    {
        var boton = Base(texto, null, Corteza, 14, FontWeight.SemiBold);
        boton.Background = Acento;
        boton.Click += (_, _) => al();
        return boton;
    }

    public static Button Secundario(string texto, Geometry? icono, Action al)
    {
        var boton = Base(texto, icono, Normal, 14, FontWeight.Normal);
        boton.Background = Superficie;
        boton.Click += (_, _) => al();
        return boton;
    }

    public static Button SecundarioAsync(string texto, Geometry? icono, Func<Task> al)
    {
        var boton = Base(texto, icono, Normal, 14, FontWeight.Normal);
        boton.Background = Superficie;
        boton.Click += async (_, _) => await al();
        return boton;
    }

    /// <summary>Sin fondo, para lo que acompaña. Separado de la sincrónica y no una sobrecarga:
    /// un lambda como `() => AlgoAsync()` encaja en Action y en Func&lt;Task&gt;, y el compilador
    /// no sabe cuál querés.</summary>
    public static Button Fantasma(string texto, Geometry? icono, Action al, IBrush? color = null)
    {
        var boton = Base(texto, icono, color ?? Subtexto, 13, FontWeight.Normal);
        boton.Background = Brushes.Transparent;
        boton.Click += (_, _) => al();
        return boton;
    }

    public static Button PrimarioAsync(string texto, Geometry? icono, Func<Task> al)
    {
        var boton = Primario(texto, icono, () => { });
        boton.Click += async (_, _) => await al();
        return boton;
    }

    static Button Base(string texto, Geometry? icono, IBrush color, double tamaño, FontWeight peso)
    {
        var contenido = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (icono != null) contenido.Children.Add(Icono(icono, tamaño + 2, color));
        contenido.Children.Add(new TextBlock
        {
            Text = texto,
            FontSize = tamaño,
            FontWeight = peso,
            Foreground = color,
            VerticalAlignment = VerticalAlignment.Center
        });

        return new Button
        {
            Content = contenido,
            Padding = new Thickness(Sangria, 0),
            MinHeight = Toque,
            CornerRadius = new CornerRadius(10),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
    }

    /// <summary>Botón sólo de ícono, cuadrado y del tamaño del dedo.</summary>
    public static Button BotonIcono(Geometry icono, Action al, IBrush? color = null,
        IBrush? fondo = null, bool relleno = false)
    {
        var boton = new Button
        {
            Content = Icono(icono, 19, color ?? Subtexto, relleno),
            Width = Toque,
            MinHeight = Toque,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Background = fondo ?? Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        boton.Click += (_, _) => al();
        return boton;
    }

    /// <summary>Una de varias opciones excluyentes: solapa, método, tipo de cuerpo. La activa va
    /// llena; en un teléfono no hay hover que ayude a saber dónde estás parado.</summary>
    public static Button Pastilla(string texto, bool activa, Action al)
    {
        var boton = new Button
        {
            Content = new TextBlock
            {
                Text = texto,
                FontSize = 13,
                FontWeight = activa ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = activa ? Normal : Tenue
            },
            Background = activa ? Superficie : Brushes.Transparent,
            Padding = new Thickness(Sangria, 0),
            MinHeight = 38,
            CornerRadius = new CornerRadius(999)
        };
        boton.Click += (_, _) => al();
        return boton;
    }

    /// <summary>Padding horizontal de los botones y las pastillas. Vale como constante porque es
    /// lo que hay que descontar cuando un botón sin fondo tiene que quedar al ras del margen.</summary>
    const double Sangria = 14;

    /// <summary>Una fila de pastillas. El fondo de la primera arranca en el borde del contenido,
    /// igual que las tarjetas y los campos: la pastilla es una caja más, y lo que tiene que
    /// alinearse es la caja, no el texto de adentro.</summary>
    public static StackPanel Pastillas(params Control[] pastillas)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var pastilla in pastillas) fila.Children.Add(pastilla);
        return fila;
    }

    /// <summary>Un fantasma que hace de enlace dentro del contenido —«Agregar variable»,
    /// «Formatear JSON»—: al ras del margen y con el color de acento.</summary>
    public static Button Enlace(string texto, Geometry? icono, Action al)
    {
        var boton = Fantasma(texto, icono, al, Acento);
        boton.HorizontalAlignment = HorizontalAlignment.Left;
        boton.Margin = new Thickness(-Sangria, 0, 0, 0);
        return boton;
    }

    // ----- Piezas -----

    /// <summary>La etiqueta del método, con su color. Ancho fijo para que los nombres de las
    /// requests queden alineados en la lista.</summary>
    public static Border EtiquetaMetodo(string metodo, double ancho = 52)
    {
        var color = ColorDeMetodo(metodo);
        return new Border
        {
            Background = Tinte(color),
            CornerRadius = new CornerRadius(4),
            Width = ancho,
            Padding = new Thickness(0, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = metodo.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
    }

    /// <summary>Estado con su punto de color: es lo primero que se mira al volver una respuesta.</summary>
    public static Border Estado(string texto, Color color)
    {
        var pincel = new SolidColorBrush(color);
        return new Border
        {
            Background = Tinte(color),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(12, 7),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new Border
                    {
                        Width = 7,
                        Height = 7,
                        CornerRadius = new CornerRadius(999),
                        Background = pincel,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = texto,
                        FontSize = 13,
                        FontWeight = FontWeight.Bold,
                        Foreground = pincel
                    }
                }
            }
        };
    }

    public static TextBox Campo(string texto, string? marca = null, bool multilinea = false,
        bool mono = false)
    {
        return new TextBox
        {
            Text = texto,
            Watermark = marca,
            AcceptsReturn = multilinea,
            TextWrapping = multilinea ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multilinea ? 120 : 44,
            FontSize = mono ? 13 : 14,
            FontFamily = mono ? "Consolas,Menlo,monospace" : FontFamily.Default,
            Background = Fondo,
            Foreground = Normal,
            BorderBrush = Superficie,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10)
        };
    }

    public static Border Tarjeta(params Control[] hijos) => Tarjeta(12, hijos);

    public static Border Tarjeta(double espacio, params Control[] hijos)
    {
        var pila = new StackPanel { Spacing = espacio };
        foreach (var hijo in hijos) pila.Children.Add(hijo);
        return new Border
        {
            Background = Panel,
            BorderBrush = Superficie,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = pila
        };
    }

    /// <summary>Aviso al pie de una sección: dice algo que conviene saber, sin gritar.</summary>
    public static Border Aviso(string texto, Color color, Geometry? icono = null)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        fila.Children.Add(Icono(icono ?? Iconos.Aviso, 16, new SolidColorBrush(color)));
        fila.Children.Add(new TextBlock
        {
            Text = texto,
            FontSize = 12,
            Foreground = new SolidColorBrush(color),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
            MaxWidth = 320
        });

        return new Border
        {
            Background = Tinte(color, 0.09),
            BorderBrush = Tinte(color, 0.28),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 11),
            Child = fila
        };
    }

    /// <summary>Una fila de lista: alto de dedo, separador abajo y nada de tarjetas. Una tarjeta
    /// por fila era lo que hacía que doscientas requests parecieran doscientos botones.</summary>
    public static Border Fila(Control contenido, Action? al = null, double alto = 60)
    {
        Control interior = contenido;
        if (al != null)
        {
            var boton = new Button
            {
                Content = contenido,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                MinHeight = alto,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                // estirado y no a la izquierda: si no, el contenido se encoge a lo que mide y una
                // grilla «*,Auto» deja el chevron pegado al texto en vez de contra el borde
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            boton.Click += (_, _) => al();
            interior = boton;
        }

        return new Border
        {
            BorderBrush = Borde,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 0),
            MinHeight = alto,
            Child = interior
        };
    }

    /// <summary>Un número con su rótulo: las métricas del runner y las cifras de una importación.
    /// El valor grande arriba y el nombre chico abajo, que es el orden en que se leen.</summary>
    public static Border Metrica(string rotulo, string valor, IBrush? color = null) => new()
    {
        Background = Panel,
        BorderBrush = Borde,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(12, 10),
        Child = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = valor,
                    FontSize = 17,
                    FontWeight = FontWeight.Bold,
                    Foreground = color ?? Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                new TextBlock { Text = rotulo, FontSize = 11, Foreground = Tenue }
            }
        }
    };

    /// <summary>Varias piezas del mismo ancho en filas: métricas, cifras. Un WrapPanel dejaría la
    /// última fila corta y desalineada, y en 393 px eso se nota.</summary>
    public static Grid Rejilla(int columnas, double espacio, params Control[] hijos)
    {
        var grilla = new Grid();
        for (var i = 0; i < columnas; i++)
            grilla.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        for (var i = 0; i < hijos.Length; i++)
        {
            var fila = i / columnas;
            var columna = i % columnas;
            while (grilla.RowDefinitions.Count <= fila)
                grilla.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            hijos[i].Margin = new Thickness(columna == 0 ? 0 : espacio / 2, fila == 0 ? 0 : espacio,
                columna == columnas - 1 ? 0 : espacio / 2, 0);
            Grid.SetRow(hijos[i], fila);
            Grid.SetColumn(hijos[i], columna);
            grilla.Children.Add(hijos[i]);
        }
        return grilla;
    }

    /// <summary>Fila de acciones que baja de renglón sola: en un teléfono angosto un StackPanel
    /// horizontal se corta.</summary>
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

    /// <summary>Encabezado de pantalla: título a la izquierda y lo que haga falta a la derecha.</summary>
    public static Border Encabezado(Control izquierda, params Control[] derecha)
    {
        var fila = new DockPanel { LastChildFill = true };
        if (derecha.Length > 0)
        {
            var acciones = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            foreach (var control in derecha) acciones.Children.Add(control);
            DockPanel.SetDock(acciones, Dock.Right);
            fila.Children.Add(acciones);
        }
        izquierda.VerticalAlignment = VerticalAlignment.Center;
        fila.Children.Add(izquierda);

        return new Border
        {
            Background = Panel,
            BorderBrush = Superficie,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 12),
            Child = fila
        };
    }
}
