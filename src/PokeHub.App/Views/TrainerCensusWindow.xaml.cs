using System.Windows;
using System.Windows.Controls;
using PokeHub.Core.Gen4;

namespace PokeHub.App.Views;

public partial class TrainerCensusWindow : Window
{
    private readonly IReadOnlyList<TrainerCensusRow> _rows;

    public TrainerCensusWindow(IReadOnlyList<TrainerCensusRow> rows)
    {
        _rows = rows;
        InitializeComponent();
        RowsGrid.ItemsSource = rows;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        RowsGrid.ItemsSource = q.Length == 0 ? _rows : _rows.Where(r => r.Place.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Map.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Region.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
