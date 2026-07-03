using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using EasyRest.Models;

namespace EasyRest.Avalonia;

/// <summary>Da a un DataGrid de KeyValueItem el comportamiento estilo Postman de "agregar fila
/// tipeando en la última fila vacía", sin ensuciar el modelo: mantiene una fila centinela vacía
/// que solo se promueve a la colección real cuando el usuario escribe algo. Delete borra la fila.</summary>
public sealed class KvGrid
{
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

    /// <summary>Vincula (o revincula) el grid a una colección. Idempotente por instancia de grid.</summary>
    public static void Bind(DataGrid grid, ObservableCollection<KeyValueItem>? source)
    {
        var helper = grid.Tag as KvGrid ?? new KvGrid(grid);
        grid.Tag = helper;
        helper.Rebind(source);
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
        // el usuario tocó la fila centinela: promoverla a fila real y crear una nueva centinela
        if (string.IsNullOrEmpty(item.Key) && string.IsNullOrEmpty(item.Value)) return;

        item.PropertyChanged -= SentinelEdited;
        _mutating = true;
        _source?.Add(item);      // ahora es una fila real del modelo (dispara dirty / rebuild URL)
        _sentinel = NewSentinel();
        _display.Add(_sentinel);
        _mutating = false;
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        if (_grid.SelectedItem is not KeyValueItem item || item == _sentinel) return;
        _mutating = true;
        _source?.Remove(item);
        _display.Remove(item);
        _mutating = false;
        e.Handled = true;
    }

    void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // cambios externos al modelo (p. ej. pegar cURL vacía y repuebla headers): reconstruir
        if (_mutating) return;
        BuildDisplay();
    }
}
