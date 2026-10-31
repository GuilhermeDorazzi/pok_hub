using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;

namespace PokeHub.App.ViewModels;

public sealed record TableChoice(int Value, string Label);

/// <summary>Uma célula editável, ligada direto aos bytes da linha.</summary>
public sealed class TableCellViewModel(TableRow row, TableColumn column, Action changed) : ObservableObject
{
    public int Value
    {
        get => row.Get(column);
        set
        {
            if (value == row.Get(column)) return;
            row.Set(column, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOn));
            changed();
        }
    }

    public bool IsOn
    {
        get => row.Get(column) != 0;
        set => Value = value ? 1 : 0;
    }
}

public sealed class TableRowViewModel(TableRow row, IReadOnlyList<TableColumn> columns, Action changed)
{
    public TableRow Row { get; } = row;
    public string Name => Row.Name;
    public IReadOnlyList<TableCellViewModel> Cells { get; } = columns.Select(c => new TableCellViewModel(row, c, changed)).ToList();
}

public sealed class TableOption(GameTable table)
{
    public GameTable Table { get; } = table;
    public string Label { get; } = Loc.T($"tables.{table.Id}");
    public string Note => Loc.T($"tables.{Table.Id}.note");
}

/// <summary>Editor único para as tabelas do jogo: as colunas vêm da definição de cada tabela.</summary>
public sealed partial class GameTablesViewModel : ObservableObject
{
    private readonly Action<string> _log;
    private readonly Func<string, Task> _snapshot;
    private readonly IReadOnlyList<string> _species, _moves, _items, _abilities;
    private List<TableRowViewModel> _allRows = [];

    public IReadOnlyList<TableOption> Tables { get; }
    public ObservableCollection<string> Owners { get; } = [];
    public ObservableCollection<TableRowViewModel> Rows { get; } = [];
    public event Action? ColumnsChanged;

    [ObservableProperty] private TableOption? _selectedTable;
    [ObservableProperty] private string? _selectedOwner;
    [ObservableProperty] private string _ownerFilter = "";
    [ObservableProperty] private string _rowFilter = "";
    [ObservableProperty] private TableRowViewModel? _selectedRow;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _status = "";

    public bool HasOwners => SelectedTable?.Table.Owners is not null;
    public bool CanResize => SelectedTable?.Table.CanResize == true;

    public GameTablesViewModel(IReadOnlyList<GameTable> tables, IReadOnlyList<string> species, IReadOnlyList<string> moves, IReadOnlyList<string> items, IReadOnlyList<string> abilities, Action<string> log, Func<string, Task> snapshot)
    {
        _species = species; _moves = moves; _items = items; _abilities = abilities; _log = log; _snapshot = snapshot;
        Tables = tables.Select(t => new TableOption(t)).OrderBy(t => t.Label).ToList();
        SelectedTable = Tables.FirstOrDefault();
    }

    /// <summary>Opções de uma coluna de escolha. Valor fora da lista continua aparecendo como número.</summary>
    public IReadOnlyList<TableChoice> ChoicesFor(TableColumn c)
    {
        IEnumerable<string> names = c.Kind switch
        {
            TableCellKind.Species => _species,
            TableCellKind.Move => _moves,
            TableCellKind.Item => _items,
            TableCellKind.Ability => _abilities,
            TableCellKind.Type => PersonalRepository.TypeNames,
            _ => Enumerable.Range(0, c.ChoiceCount).Select(i => Loc.T($"{c.ChoicePrefix}{i}")),
        };
        return names.Select((n, i) => new TableChoice(i, c.Kind is TableCellKind.Choice or TableCellKind.Type ? n : $"{i:D3} {n}")).ToList();
    }

    partial void OnSelectedTableChanged(TableOption? value)
    {
        OnPropertyChanged(nameof(HasOwners));
        OnPropertyChanged(nameof(CanResize));
        ColumnsChanged?.Invoke();
        OwnerFilter = "";
        RowFilter = "";
        ApplyOwnerFilter();
        SelectedOwner = Owners.FirstOrDefault();
        LoadRows();
    }

    partial void OnOwnerFilterChanged(string value) => ApplyOwnerFilter();
    partial void OnRowFilterChanged(string value) => ApplyRowFilter();
    partial void OnSelectedOwnerChanged(string? value) => LoadRows();

    private void ApplyOwnerFilter()
    {
        var keep = SelectedOwner;
        Owners.Clear();
        if (SelectedTable?.Table.Owners is not { } owners) return;
        foreach (var o in owners.Where(o => string.IsNullOrWhiteSpace(OwnerFilter) || o.Contains(OwnerFilter, StringComparison.OrdinalIgnoreCase))) Owners.Add(o);
        if (keep is not null && Owners.Contains(keep)) SelectedOwner = keep;
    }

    private int OwnerIndex => SelectedTable?.Table.Owners is { } owners && SelectedOwner is { } o ? IndexOf(owners, o) : 0;

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == value) return i;
        return -1;
    }

    private void LoadRows()
    {
        if (SelectedTable is not { } option || OwnerIndex < 0 || (HasOwners && SelectedOwner is null))
        {
            _allRows = [];
            ApplyRowFilter();
            return;
        }
        _allRows = option.Table.Rows(OwnerIndex).Select(r => new TableRowViewModel(r, option.Table.Columns, MarkDirty)).ToList();
        ApplyRowFilter();
    }

    private void ApplyRowFilter()
    {
        Rows.Clear();
        foreach (var r in _allRows.Where(r => string.IsNullOrWhiteSpace(RowFilter) || r.Name.Contains(RowFilter, StringComparison.OrdinalIgnoreCase))) Rows.Add(r);
    }

    private void MarkDirty()
    {
        IsDirty = true;
        Status = Loc.T("common.unsaved");
    }

    [RelayCommand]
    private void AddRow()
    {
        if (SelectedTable is not { Table.CanResize: true } option) return;
        try
        {
            option.Table.AddRow(OwnerIndex);
            MarkDirty();
            LoadRows();
            SelectedRow = Rows.LastOrDefault();
        }
        catch (InvalidOperationException ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private void RemoveRow()
    {
        if (SelectedTable is not { Table.CanResize: true } option || SelectedRow is not { } row) return;
        int index = _allRows.IndexOf(row);
        if (index < 0) return;
        option.Table.RemoveRow(OwnerIndex, index);
        MarkDirty();
        LoadRows();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        foreach (var option in Tables)
            if (option.Table.Validate() is { } error) { Status = $"{option.Label}: {error}"; return; }
        try
        {
            var changed = Tables.Where(o => o.Table.HasChanges).ToList();
            foreach (var option in changed) option.Table.Save();
            IsDirty = false;
            Status = Loc.T("common.savedAt", DateTime.Now.ToString("HH:mm:ss"));
            if (changed.Count == 0) return;
            var names = string.Join(", ", changed.Select(o => o.Label));
            _log(Loc.T("tables.saved", names));
            await _snapshot(Loc.T("tables.snapshot", names));
            LoadRows();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = Loc.T("common.saveError", ex.Message);
        }
    }
}
