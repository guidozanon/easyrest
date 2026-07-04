using System.Collections.ObjectModel;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest;

/// <summary>Una corrida guardada dentro de la lista de comparación, con su check de selección.</summary>
public class ComparisonEntry : Observable
{
    bool _selected;

    public ComparisonEntry(RunRecord record) => Record = record;

    public RunRecord Record { get; }
    public bool Selected { get => _selected; set => Set(ref _selected, value); }

    public string Title => string.IsNullOrEmpty(Record.SavedAt) ? Record.Label : $"{Record.SavedAt} · {Record.Label}";
    public string Sub => $"{Record.Total} req · {Record.ErrorRate:0.#}% err · pico {Record.PeakRps} req/s · avg {Record.Avg:0}ms";
}

/// <summary>Pestaña para comparar corridas guardadas: se eligen con checks y se ven en tabla + gráfico.</summary>
public class RunComparisonTab : Observable
{
    public ObservableCollection<ComparisonEntry> Runs { get; } = new();

    /// <summary>Se dispara cuando cambia la selección o la lista (para refrescar tabla y gráfico).</summary>
    public event Action? SelectionChanged;

    public RunComparisonTab() => Reload();

    public void Reload()
    {
        foreach (var e in Runs) e.PropertyChanged -= OnEntryChanged;
        Runs.Clear();
        foreach (var r in Storage.LoadRuns())
        {
            var entry = new ComparisonEntry(r);
            entry.PropertyChanged += OnEntryChanged;
            Runs.Add(entry);
        }
        SelectionChanged?.Invoke();
    }

    void OnEntryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComparisonEntry.Selected)) SelectionChanged?.Invoke();
    }

    public List<RunRecord> Selected => Runs.Where(r => r.Selected).Select(r => r.Record).ToList();

    public void Delete(ComparisonEntry entry)
    {
        Storage.DeleteRun(entry.Record.Id);
        entry.PropertyChanged -= OnEntryChanged;
        Runs.Remove(entry);
        SelectionChanged?.Invoke();
    }
}
