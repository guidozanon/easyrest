using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest.Models;

namespace EasyRest.Avalonia;

/// <summary>Da a un DataGrid de KeyValueItem el comportamiento estilo Postman: agregar fila
/// tipeando en la última fila (centinela) sin ensuciar el modelo, checkbox de un solo click, y
/// un tacho de basura que aparece al pasar el mouse por la fila para borrarla. Delete también borra.</summary>
public sealed class KvGrid
{
    static readonly IBrush Overlay = Brush.Parse("#6C7086");

    readonly DataGrid _grid;
    ObservableCollection<KeyValueItem>? _source;
    ObservableCollection<KeyValueItem> _display = new();
    KeyValueItem? _sentinel;
    bool _mutating;

    KvGrid(DataGrid grid)
    {
        _grid = grid;
        _grid.KeyDown += OnKeyDown;
    }

    public static void Bind(DataGrid grid, ObservableCollection<KeyValueItem>? source,
        string keyHeader = "CLAVE", string valueHeader = "VALOR")
    {
        var helper = grid.Tag as KvGrid ?? new KvGrid(grid);
        grid.Tag = helper;
        helper.BuildColumns(keyHeader, valueHeader);
        helper.Rebind(source);
    }

    void BuildColumns(string keyHeader, string valueHeader)
    {
        if (_grid.Columns.Count > 0) return;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.CanUserResizeColumns = true;

        // Enabled: checkbox en CellTemplate → toggle en un solo click, sin editar la celda
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "",
            Width = new DataGridLength(34),
            CellTemplate = new FuncDataTemplate<KeyValueItem>((_, _) =>
            {
                var cb = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0) };
                cb.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(KeyValueItem.Enabled)) { Mode = BindingMode.TwoWay });
                return cb;
            })
        });

        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = keyHeader,
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(KeyValueItem.Key)) { Mode = BindingMode.TwoWay }
        });
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = valueHeader,
            Width = new DataGridLength(3, DataGridLengthUnitType.Star),
            Binding = new Binding(nameof(KeyValueItem.Value)) { Mode = BindingMode.TwoWay }
        });

        // Tacho: visible solo al pasar el mouse por la fila
        _grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "",
            Width = new DataGridLength(34),
            CellTemplate = new FuncDataTemplate<KeyValueItem>((item, _) =>
            {
                var btn = new Button
                {
                    Content = "🗑",
                    FontSize = 12,
                    Padding = new(4, 0),
                    Background = Brushes.Transparent,
                    BorderThickness = new(0),
                    Foreground = Overlay,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                btn.Click += (_, _) => { if (item != null) RemoveRow(item); };
                // aparece cuando el puntero está sobre la fila
                btn.Bind(global::Avalonia.Visual.IsVisibleProperty, new Binding("IsPointerOver")
                {
                    RelativeSource = new RelativeSource { AncestorType = typeof(DataGridRow) }
                });
                return btn;
            })
        });
    }

    void Rebind(ObservableCollection<KeyValueItem>? source)
    {
        if (_source != null) _source.CollectionChanged -= OnSourceChanged;
        _source = source;
        BuildDisplay();
        if (_source != null) _source.CollectionChanged += OnSourceChanged;
    }

    void BuildDisplay()
    {
        _mutating = true;
        _display = new ObservableCollection<KeyValueItem>();
        if (_source != null)
            foreach (var item in _source) _display.Add(item);
        _sentinel = NewSentinel();
        _display.Add(_sentinel);
        _grid.ItemsSource = _display;
        _mutating = false;
    }

    KeyValueItem NewSentinel()
    {
        var item = new KeyValueItem { Enabled = true };
        item.PropertyChanged += SentinelEdited;
        return item;
    }

    void SentinelEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (_mutating || sender is not KeyValueItem item || item != _sentinel) return;
        if (string.IsNullOrEmpty(item.Key) && string.IsNullOrEmpty(item.Value)) return;

        item.PropertyChanged -= SentinelEdited;
        _mutating = true;
        _source?.Add(item);
        _sentinel = NewSentinel();
        _display.Add(_sentinel);
        _mutating = false;
    }

    void RemoveRow(KeyValueItem item)
    {
        if (item == _sentinel) return;
        _mutating = true;
        _source?.Remove(item);
        _display.Remove(item);
        _mutating = false;
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        if (_grid.SelectedItem is not KeyValueItem item || item == _sentinel) return;
        RemoveRow(item);
        e.Handled = true;
    }

    void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_mutating) return;
        BuildDisplay();
    }
}
