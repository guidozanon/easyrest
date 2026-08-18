using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest.Models;
using EasyRest.Services;

using Button = Avalonia.Controls.Button;
using CheckBox = Avalonia.Controls.CheckBox;
using Orientation = Avalonia.Layout.Orientation;

namespace EasyRest.Android;

/// <summary>Ambientes y sus variables, que es lo que hace que la misma colección sirva contra
/// producción y contra la de pruebas sin editar una URL.
///
/// Los ambientes son pastillas arriba y el activo va lleno y con su punto: en una pantalla chica,
/// el ambiente contra el que estás mandando tiene que verse sin buscarlo, porque equivocarse de
/// ambiente es el error caro.
///
/// Las variables son filas y no una grilla de campos: dos campos de texto y tres botones en 393 px
/// dejaban ocho caracteres visibles por campo. La fila muestra clave y valor —el valor en
/// monoespaciada, que es como se lee un token— y editarla abre un formulario donde entran.
///
/// Una variable se puede marcar como secreta y entonces su valor va enmascarado, con un botón para
/// verlo. No es criptografía —en el disco del teléfono sigue estando en claro, como en el
/// escritorio—: es que un token no tiene por qué quedar a la vista mientras mostrás la pantalla en
/// una reunión o en el subte. La marca se guarda con el mismo nombre (<c>secretKeys</c>) que usa el
/// documento que viaja al servidor de sync, así que el día que la app cifre ambientes ya está
/// expresado qué hay que cifrar.</summary>
internal class EnvironmentsView : UserControl
{
    readonly Action _alCambiar;
    readonly StackPanel _pila = new() { Margin = new Thickness(0, 0, 0, 24) };

    readonly List<EnvironmentModel> _ambientes;
    EnvironmentModel? _actual;

    public EnvironmentsView(List<EnvironmentModel> ambientes, EnvironmentModel? activo, Action alCambiar)
    {
        _ambientes = ambientes;
        _actual = activo ?? ambientes.FirstOrDefault();
        _alCambiar = alCambiar;

        var nuevo = Ui.Secundario("Nuevo", Iconos.Mas, NuevoAmbiente);
        nuevo.MinHeight = 38;
        var menú = Ui.BotonIcono(Iconos.Puntos, MenúDelAmbiente, relleno: true);

        var raíz = new DockPanel();
        var encabezado = Ui.Encabezado(Ui.Titulo("Ambientes"), nuevo, menú);
        DockPanel.SetDock(encabezado, Dock.Top);
        raíz.Children.Add(encabezado);
        raíz.Children.Add(new ScrollViewer { Content = _pila });

        Content = raíz;
        Redibujar();
    }

    void Guardar()
    {
        Storage.SaveEnvironments(_ambientes);
        _alCambiar();
    }

    void Redibujar()
    {
        _pila.Children.Clear();

        if (_ambientes.Count == 0)
        {
            _pila.Children.Add(Vacío());
            return;
        }

        _pila.Children.Add(Selector());

        var variables = _actual?.Variables ?? new System.Collections.ObjectModel.ObservableCollection<KeyValueItem>();
        var rótulo = Ui.Rotulo($"Variables · {variables.Count}");
        rótulo.Margin = new Thickness(16, 16, 16, 10);
        _pila.Children.Add(rótulo);

        foreach (var variable in variables.ToList()) _pila.Children.Add(Fila(variable));

        var agregar = Ui.Enlace("Agregar variable", Iconos.Mas, () =>
        {
            if (_actual == null) return;
            _actual.Variables.Add(new KeyValueItem { Key = "nueva", Value = "" });
            Guardar();
            Redibujar();
        });
        // 16 de margen de pantalla, menos lo que el propio botón mete de padding: así el «+»
        // arranca en la misma vertical que los nombres de las variables
        agregar.Margin = new Thickness(16 - 14, 4, 0, 0);
        _pila.Children.Add(agregar);

        var aviso = Ui.Aviso(
            "Marcar una variable como secreta la esconde en pantalla y la deja anotada como secreta " +
            "para el servidor. En el disco del teléfono se guarda en claro, igual que en el escritorio.",
            Ui.CAmarillo, Iconos.Candado);
        aviso.Margin = new Thickness(16, 14, 16, 0);
        _pila.Children.Add(aviso);

        var ayuda = Ui.Nota("Usalas con {{nombre}} en la URL, las cabeceras, la auth o el cuerpo.");
        ayuda.Margin = new Thickness(16, 12, 16, 0);
        _pila.Children.Add(ayuda);
    }

    Control Vacío()
    {
        var pila = new StackPanel { Spacing = 14, Margin = new Thickness(24, 48, 24, 24) };
        var icono = Ui.Icono(Iconos.Globo, 40, Ui.Superficie);
        icono.HorizontalAlignment = HorizontalAlignment.Center;
        pila.Children.Add(icono);

        var titulo = Ui.Parrafo("Todavía no hay ambientes", Ui.Subtexto, 15);
        titulo.TextAlignment = TextAlignment.Center;
        pila.Children.Add(titulo);

        var texto = Ui.Parrafo(
            "Un ambiente guarda los valores que cambian entre producción y pruebas: la URL base, un " +
            "token, el tenant. Después los usás con {{nombre}} en cualquier parte de la request.",
            Ui.Tenue, 13);
        texto.TextAlignment = TextAlignment.Center;
        pila.Children.Add(texto);

        var crear = Ui.Primario("Crear el primero", Iconos.Mas, NuevoAmbiente);
        crear.Margin = new Thickness(0, 8, 0, 0);
        pila.Children.Add(crear);
        return pila;
    }

    /// <summary>Las pastillas de arriba. El activo va relleno y con punto verde; los demás,
    /// contorneados. Es la única señal de contra qué se está mandando.</summary>
    Control Selector()
    {
        var fila = new WrapPanel { Margin = new Thickness(16, 14, 16, 0) };
        foreach (var ambiente in _ambientes)
        {
            var cual = ambiente;
            var activo = ReferenceEquals(ambiente, _actual);

            var contenido = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (activo)
                contenido.Children.Add(new Border
                {
                    Width = 7,
                    Height = 7,
                    CornerRadius = new CornerRadius(999),
                    Background = Ui.Verde,
                    VerticalAlignment = VerticalAlignment.Center
                });
            contenido.Children.Add(new TextBlock
            {
                Text = ambiente.Name,
                FontSize = 13,
                FontWeight = activo ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = activo ? Ui.Acento : Ui.Subtexto
            });

            var pastilla = new Button
            {
                Content = contenido,
                Background = activo ? Ui.Tinte(Ui.CAcento, 0.14) : Brushes.Transparent,
                BorderBrush = activo ? Ui.Acento : Ui.Superficie,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(14, 0),
                MinHeight = 40,
                Margin = new Thickness(0, 0, 8, 8)
            };
            pastilla.Click += (_, _) =>
            {
                _actual = cual;
                Storage.SetActiveEnvironmentId(cual.Id);
                _alCambiar();
                Redibujar();
            };
            fila.Children.Add(pastilla);
        }
        return fila;
    }

    Control Fila(KeyValueItem variable)
    {
        var ambiente = _actual!;
        var secreta = ambiente.IsSecret(variable.Key);

        var clave = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        clave.Children.Add(Ui.Mono(variable.Key, Ui.Acento, 13));
        if (secreta) clave.Children.Add(Ui.IconoDeTexto(Iconos.Candado, 12, Ui.Amarillo));

        // enmascarar acá y no con PasswordChar: la fila es texto, no un campo, y un largo fijo
        // tampoco delata cuántos caracteres tiene el token
        var valor = Ui.Mono(
            secreta ? new string('•', 18) : Mostrable(variable.Value),
            secreta ? Ui.Tenue : Ui.Normal, 13);
        valor.TextWrapping = TextWrapping.NoWrap;
        valor.TextTrimming = TextTrimming.CharacterEllipsis;

        var textos = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { clave, valor }
        };

        var boton = new Button
        {
            Content = textos,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            MinHeight = 64,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        boton.Click += (_, _) => Editar(variable);

        var acciones = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (secreta)
            acciones.Children.Add(Ui.BotonIcono(Iconos.Ojo,
                () => Dialogo.Confirmar($"«{variable.Key}»", Mostrable(variable.Value), "Listo", () => { }),
                Ui.Tenue, Ui.Borde));
        acciones.Children.Add(Ui.BotonIcono(Iconos.Basura, () => Eliminar(variable), Ui.Tenue));

        var contenido = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(boton, 0);
        Grid.SetColumn(acciones, 1);
        contenido.Children.Add(boton);
        contenido.Children.Add(acciones);

        return new Border
        {
            BorderBrush = Ui.Borde,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 0),
            MinHeight = 64,
            Child = contenido
        };
    }

    static string Mostrable(string valor) =>
        string.IsNullOrWhiteSpace(valor) ? "(vacío)" : valor;

    /// <summary>Clave, valor y si es secreta, los tres juntos: separarlos en tres gestos era lo
    /// que hacía que cambiar un token fueran cinco toques.</summary>
    void Editar(KeyValueItem variable)
    {
        var ambiente = _actual!;
        var eraSecreta = ambiente.IsSecret(variable.Key);
        var claveAnterior = variable.Key;

        var clave = Ui.Campo(variable.Key, "nombre", mono: true);
        var valor = Ui.Campo(variable.Value, "valor", multilinea: true, mono: true);
        valor.MinHeight = 90;

        var secreta = new CheckBox
        {
            Content = new TextBlock { Text = "Secreta", FontSize = 13, Foreground = Ui.Normal },
            IsChecked = eraSecreta,
            MinHeight = Ui.Toque
        };

        var formulario = new StackPanel
        {
            Spacing = 10,
            Children = { Ui.Rotulo("Nombre"), clave, Ui.Rotulo("Valor"), valor, secreta }
        };

        Dialogo.Formulario($"Variable «{variable.Key}»", formulario, "Guardar", () =>
        {
            var nueva = (clave.Text ?? "").Trim();
            if (nueva.Length == 0) return;

            // la marca de secreto está indexada por nombre: si el nombre cambia hay que moverla,
            // o el valor quedaría a la vista sin que nadie lo haya pedido
            ambiente.SetSecret(claveAnterior, false);
            variable.Key = nueva;
            variable.Value = valor.Text ?? "";
            if (secreta.IsChecked == true) ambiente.SetSecret(nueva, true);

            Guardar();
            Redibujar();
        });
    }

    void Eliminar(KeyValueItem variable)
    {
        var ambiente = _actual!;
        Dialogo.Confirmar($"Eliminar «{variable.Key}»",
            "Se borra la variable de este ambiente. No se puede deshacer.", "Eliminar", () =>
            {
                ambiente.SetSecret(variable.Key, false);
                ambiente.Variables.Remove(variable);
                Guardar();
                Redibujar();
            });
    }

    void MenúDelAmbiente()
    {
        if (_actual == null) { NuevoAmbiente(); return; }
        Dialogo.Opciones(_actual.Name,
            ("Nuevo ambiente", NuevoAmbiente),
            ("Renombrar", Renombrar),
            ("Duplicar", Duplicar),
            ("Eliminar", Eliminar));
    }

    void NuevoAmbiente() => Dialogo.Texto("Nuevo ambiente", "", "nombre", nombre =>
    {
        var ambiente = new EnvironmentModel { Name = nombre };
        _ambientes.Add(ambiente);
        _actual = ambiente;
        Storage.SetActiveEnvironmentId(ambiente.Id);
        Guardar();
        Redibujar();
    });

    void Renombrar()
    {
        if (_actual == null) return;
        var ambiente = _actual;
        Dialogo.Texto("Renombrar ambiente", ambiente.Name, "nombre", nombre =>
        {
            ambiente.Name = nombre;
            Guardar();
            Redibujar();
        });
    }

    /// <summary>Duplicar es cómo se arma «staging» a partir de «producción» sin volver a tipear
    /// diez variables. Se copia a mano y no por serialización, por el trimming del head.</summary>
    void Duplicar()
    {
        if (_actual == null) return;
        var original = _actual;
        var copia = new EnvironmentModel { Name = original.Name + " (copia)" };
        foreach (var variable in original.Variables)
            copia.Variables.Add(new KeyValueItem
            {
                Enabled = variable.Enabled,
                Key = variable.Key,
                Value = variable.Value
            });
        foreach (var clave in original.SecretKeys) copia.SetSecret(clave, true);

        _ambientes.Add(copia);
        _actual = copia;
        Storage.SetActiveEnvironmentId(copia.Id);
        Guardar();
        Redibujar();
    }

    void Eliminar()
    {
        if (_actual == null) return;
        var ambiente = _actual;
        Dialogo.Confirmar("Eliminar ambiente", $"«{ambiente.Name}» y sus variables. No se puede deshacer.",
            "Eliminar", () =>
            {
                _ambientes.Remove(ambiente);
                _actual = _ambientes.FirstOrDefault();
                Storage.SetActiveEnvironmentId(_actual?.Id);
                Guardar();
                Redibujar();
            });
    }
}
