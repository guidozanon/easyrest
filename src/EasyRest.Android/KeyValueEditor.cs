using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using EasyRest.Models;

using Button = Avalonia.Controls.Button;
using CheckBox = Avalonia.Controls.CheckBox;

namespace EasyRest.Android;

/// <summary>Editor de filas clave/valor: query params, cabeceras y el cuerpo form-urlencoded.
///
/// Escribe directo sobre los <see cref="KeyValueItem"/> del modelo, igual que el escritorio: la
/// pantalla no tiene una copia que después haya que reconciliar. Lo que decide si el cambio llega
/// al disco es el botón Guardar del editor, no esta clase.
///
/// El check de habilitado va primero y ancho: es lo que más se toca —prender y apagar un header
/// para probar— y en un teléfono un check de 16 píxeles al lado de un campo de texto es una
/// trampa.</summary>
internal class KeyValueEditor : UserControl
{
    readonly ObservableCollection<KeyValueItem> _items;
    readonly Action _alCambiar;
    readonly string _marcaClave;
    readonly string _marcaValor;
    readonly StackPanel _filas = new() { Spacing = 6 };

    public KeyValueEditor(ObservableCollection<KeyValueItem> items, Action alCambiar,
        string marcaClave = "clave", string marcaValor = "valor")
    {
        _items = items;
        _alCambiar = alCambiar;
        _marcaClave = marcaClave;
        _marcaValor = marcaValor;

        var agregar = Ui.Accion("+ Agregar", () =>
        {
            _items.Add(new KeyValueItem());
            Redibujar();
            _alCambiar();
        });
        agregar.HorizontalAlignment = HorizontalAlignment.Left;

        Content = new StackPanel
        {
            Spacing = 8,
            Children = { _filas, agregar }
        };

        Redibujar();
    }

    void Redibujar()
    {
        _filas.Children.Clear();
        if (_items.Count == 0)
        {
            _filas.Children.Add(Ui.Rotulo("(vacío)"));
            return;
        }
        foreach (var item in _items) _filas.Children.Add(Fila(item));
    }

    Control Fila(KeyValueItem item)
    {
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

        var clave = Ui.Campo(item.Key, _marcaClave);
        clave.TextChanged += (_, _) =>
        {
            item.Key = clave.Text ?? "";
            _alCambiar();
        };

        var valor = Ui.Campo(item.Value, _marcaValor);
        valor.TextChanged += (_, _) =>
        {
            item.Value = valor.Text ?? "";
            _alCambiar();
        };

        var borrar = new Button
        {
            Content = "✕",
            MinHeight = Ui.Toque,
            MinWidth = Ui.Toque,
            Padding = new Thickness(6, 0),
            Foreground = Ui.Tenue
        };
        borrar.Click += (_, _) =>
        {
            _items.Remove(item);
            Redibujar();
            _alCambiar();
        };

        // clave y valor a mitades: en un teléfono no entra nada más, y en tablet se estiran las dos
        var grilla = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*,Auto") };
        Grid.SetColumn(habilitado, 0);
        Grid.SetColumn(clave, 1);
        Grid.SetColumn(valor, 2);
        Grid.SetColumn(borrar, 3);
        clave.Margin = new Thickness(4, 0);
        valor.Margin = new Thickness(0, 0, 4, 0);
        grilla.Children.Add(habilitado);
        grilla.Children.Add(clave);
        grilla.Children.Add(valor);
        grilla.Children.Add(borrar);
        return grilla;
    }
}
