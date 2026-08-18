using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using EasyRest.Models;

using CheckBox = Avalonia.Controls.CheckBox;

namespace EasyRest.Android;

/// <summary>Editor de filas clave/valor: query params, cabeceras y el cuerpo form-urlencoded.
///
/// Escribe directo sobre los <see cref="KeyValueItem"/> del modelo, igual que el escritorio: la
/// pantalla no tiene una copia que después haya que reconciliar. Lo que decide si el cambio llega
/// al disco es el botón Guardar del editor, no esta clase.
///
/// Cada fila es una tarjeta con la clave arriba y el valor abajo, en vez de dos campos angostos
/// uno al lado del otro: en 393 px de ancho, dos campos y dos botones dejaban ocho caracteres
/// visibles por campo.</summary>
internal class KeyValueEditor : UserControl
{
    readonly ObservableCollection<KeyValueItem> _items;
    readonly Action _alCambiar;
    readonly string _marcaClave;
    readonly string _marcaValor;
    readonly StackPanel _filas = new() { Spacing = 8 };

    public KeyValueEditor(ObservableCollection<KeyValueItem> items, Action alCambiar,
        string marcaClave = "clave", string marcaValor = "valor")
    {
        _items = items;
        _alCambiar = alCambiar;
        _marcaClave = marcaClave;
        _marcaValor = marcaValor;

        var agregar = Ui.Enlace("Agregar", Iconos.Mas, () =>
        {
            _items.Add(new KeyValueItem());
            Redibujar();
            _alCambiar();
        });

        Content = new StackPanel { Spacing = 10, Children = { _filas, agregar } };
        Redibujar();
    }

    void Redibujar()
    {
        _filas.Children.Clear();
        if (_items.Count == 0)
        {
            _filas.Children.Add(Ui.Nota("Todavía no hay ninguna."));
            return;
        }
        foreach (var item in _items.ToList()) _filas.Children.Add(Fila(item));
    }

    Control Fila(KeyValueItem item)
    {
        // el check primero y grande: prender y apagar es lo que más se toca, y un check de 16 px
        // al lado de un campo de texto es una trampa en una pantalla táctil
        var habilitado = new CheckBox
        {
            IsChecked = item.Enabled,
            MinHeight = Ui.Toque,
            VerticalAlignment = VerticalAlignment.Center
        };
        habilitado.IsCheckedChanged += (_, _) =>
        {
            item.Enabled = habilitado.IsChecked == true;
            _alCambiar();
        };

        var clave = Ui.Campo(item.Key, _marcaClave, mono: true);
        clave.TextChanged += (_, _) => { item.Key = clave.Text ?? ""; _alCambiar(); };

        var valor = Ui.Campo(item.Value, _marcaValor, mono: true);
        valor.TextChanged += (_, _) => { item.Value = valor.Text ?? ""; _alCambiar(); };

        var borrar = Ui.BotonIcono(Iconos.Basura, () =>
        {
            _items.Remove(item);
            Redibujar();
            _alCambiar();
        }, Ui.Tenue);

        var encabezado = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(habilitado, 0);
        Grid.SetColumn(clave, 1);
        Grid.SetColumn(borrar, 2);
        clave.Margin = new Thickness(2, 0, 4, 0);
        encabezado.Children.Add(habilitado);
        encabezado.Children.Add(clave);
        encabezado.Children.Add(borrar);

        return new Border
        {
            Background = Ui.Panel,
            BorderBrush = Ui.Superficie,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8),
            Child = new StackPanel { Spacing = 8, Children = { encabezado, valor } }
        };
    }
}
