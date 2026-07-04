using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyRest.Models;

namespace EasyRest.Avalonia.Views;

/// <summary>Editor de pares clave/valor estilo Postman: fila centinela para agregar tipeando (sin
/// ensuciar el modelo hasta que hay contenido), checkbox de un solo click y tacho al pasar el mouse.
/// Basado en ItemsControl para evitar la fricción de selección de celdas del DataGrid.</summary>
public partial class KvEditor : UserControl
{
    ObservableCollection<KeyValueItem>? _source;
    ObservableCollection<KeyValueItem> _display = new();
    KeyValueItem? _sentinel;
    bool _mutating;

    public KvEditor() => InitializeComponent();

    /// <summary>Vincula (o revincula) el editor a una colección del modelo.</summary>
    public void Bind(ObservableCollection<KeyValueItem>? source, string keyHeader, string valueHeader)
    {
        KeyHeader.Text = keyHeader;
        ValueHeader.Text = valueHeader;
        if (_source != null) _source.CollectionChanged -= OnSourceChanged;
        _source = source;
        BuildDisplay();
        if (_source != null) _source.CollectionChanged += OnSourceChanged;
    }

    void BuildDisplay()
    {
        _mutating = true;
        if (_sentinel != null) _sentinel.PropertyChanged -= SentinelEdited;
        _display = new ObservableCollection<KeyValueItem>();
        if (_source != null)
            foreach (var item in _source) _display.Add(item);
        _sentinel = NewSentinel();
        _display.Add(_sentinel);
        Rows.ItemsSource = _display;
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
        _source?.Add(item);           // ahora es fila real del modelo (dispara dirty / rebuild URL)
        _sentinel = NewSentinel();
        _display.Add(_sentinel);
        _mutating = false;
    }

    void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not KeyValueItem item || item == _sentinel) return;
        _mutating = true;
        _source?.Remove(item);
        _display.Remove(item);
        _mutating = false;
    }

    void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // cambios externos al modelo (p. ej. pegar cURL repuebla headers): reconstruir
        if (_mutating) return;
        BuildDisplay();
    }
}
