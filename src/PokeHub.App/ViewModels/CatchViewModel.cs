using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;

namespace PokeHub.App.ViewModels;

public sealed record CatchItem(string Species, CatchStatus Status, string StatusText, string Where, string Note);

/// <summary>Tela só de leitura: o que dá para obter no jogo e o que ainda falta, com filtro por situação e por texto.</summary>
public sealed partial class CatchViewModel : ObservableObject
{
    public const int FilterAll = 0, FilterMissing = 1, FilterUnsure = 2;

    public ICollectionView Rows { get; }
    public string Summary { get; }
    public IReadOnlyList<string> Filters { get; } = [Loc.T("catch.filter.all"), Loc.T("catch.filter.missing"), Loc.T("catch.filter.unsure")];

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private int _filter = FilterMissing;

    public CatchViewModel(IReadOnlyList<CatchRow> rows)
    {
        var items = rows.Select(r => new CatchItem($"{r.Species:D3} {r.Name}", r.Status, Loc.T(StatusKey(r.Status)), r.Where, r.Note)).ToList();
        int Count(CatchStatus s) => rows.Count(r => r.Status == s);
        Summary = Loc.T("catch.summary", Count(CatchStatus.Found), Count(CatchStatus.Family), Count(CatchStatus.Known), Count(CatchStatus.Missing), rows.Count);
        Rows = CollectionViewSource.GetDefaultView(items);
        Rows.Filter = o => Matches((CatchItem)o);
    }

    private static string StatusKey(CatchStatus s) => s switch
    {
        CatchStatus.Found => "catch.status.found",
        CatchStatus.Family => "catch.status.family",
        CatchStatus.Known => "catch.status.known",
        _ => "catch.status.missing",
    };

    private bool Matches(CatchItem item)
    {
        if (Filter == FilterMissing && item.Status != CatchStatus.Missing) return false;
        if (Filter == FilterUnsure && item.Status is not (CatchStatus.Missing or CatchStatus.Known)) return false;
        var q = Search.Trim();
        return q.Length == 0 || item.Species.Contains(q, StringComparison.OrdinalIgnoreCase) || item.Where.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSearchChanged(string value) => Rows.Refresh();
    partial void OnFilterChanged(int value) => Rows.Refresh();
}
