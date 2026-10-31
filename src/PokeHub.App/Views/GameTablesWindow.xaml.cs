using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PokeHub.App.ViewModels;
using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;

namespace PokeHub.App.Views;

public partial class GameTablesWindow : Window
{
    private readonly GameTablesViewModel _vm;

    public GameTablesWindow(GameTablesViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        vm.ColumnsChanged += BuildColumns;
        Closed += (_, _) => vm.ColumnsChanged -= BuildColumns;
        BuildColumns();
    }

    /// <summary>Cada tabela tem colunas próprias, então a grade é montada a partir da definição dela.</summary>
    private void BuildColumns()
    {
        Grid.Columns.Clear();
        if (_vm.SelectedTable is not { } option) return;
        Grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("tables.col.name"), Binding = new Binding(nameof(TableRowViewModel.Name)), IsReadOnly = true, Width = new DataGridLength(190) });
        var columns = option.Table.Columns;
        for (int i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            string header = Loc.T($"tables.col.{c.Key}");
            string cell = $"Cells[{i}]";
            switch (c.Kind)
            {
                case TableCellKind.Flag:
                    Grid.Columns.Add(new DataGridCheckBoxColumn { Header = header, Binding = new Binding($"{cell}.IsOn") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged } });
                    break;
                case TableCellKind.Number:
                    Grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding($"{cell}.Value") { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus } });
                    break;
                default:
                    Grid.Columns.Add(new DataGridComboBoxColumn
                    {
                        Header = header,
                        ItemsSource = _vm.ChoicesFor(c),
                        DisplayMemberPath = nameof(TableChoice.Label),
                        SelectedValuePath = nameof(TableChoice.Value),
                        SelectedValueBinding = new Binding($"{cell}.Value") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                        MinWidth = 150,
                    });
                    break;
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_vm.IsDirty) return;
        var answer = MessageBox.Show(this, Loc.T("common.closeConfirm"), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) e.Cancel = true;
    }
}
