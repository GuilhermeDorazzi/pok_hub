using System.Windows;
using System.Windows.Controls;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;
using PokeHub.Core.Reports;

namespace PokeHub.App.Views;

public partial class HistoryWindow : Window
{
    private readonly WorkspaceGit _git;
    private readonly Project _project;
    private readonly Action<string> _log;
    private readonly Func<string, Task<IReadOnlyList<ChangeLine>>> _compare;

    private sealed record Row(string Hash, string Message);

    /// <summary>true se alguma versão foi restaurada enquanto a janela esteve aberta.</summary>
    public bool Restored { get; private set; }

    public HistoryWindow(WorkspaceGit git, Project project, Action<string> log, Func<string, Task<IReadOnlyList<ChangeLine>>> compare)
    {
        _compare = compare;
        _git = git;
        _project = project;
        _log = log;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        StatusText.Text = Loc.T("hist.loading");
        var history = await _git.HistoryAsync(_project, 100);
        VersionsList.ItemsSource = history.Select(h => new Row(h.Hash, h.Message)).ToList();
        StatusText.Text = history.Count == 0 ? Loc.T("hist.none") : Loc.T("hist.count", history.Count);
    }

    private void VersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RestoreButton.IsEnabled = VersionsList.SelectedItem is Row && VersionsList.SelectedIndex > 0;
        CompareButton.IsEnabled = VersionsList.SelectedItem is Row;
    }

    /// <summary>Compara a versão selecionada com o workspace atual e mostra o relatório em termos de jogo.</summary>
    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (VersionsList.SelectedItem is not Row row) return;
        try
        {
            IsEnabled = false;
            StatusText.Text = Loc.T("hist.comparing");
            var lines = await _compare(row.Hash);
            new ChangeReportWindow(Loc.T("report.intro", row.Hash, row.Message), lines, PokeHub.App.ViewModels.SpeciesCatalog.TryForProject(_project)) { Owner = this }.ShowDialog();
            StatusText.Text = Loc.T("hist.count", VersionsList.Items.Count);
        }
        catch (Exception ex)
        {
            _log(Loc.T("hist.error", row.Hash, ex.Message));
            MessageBox.Show(this, ex.Message, Loc.T("hist.errorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (VersionsList.SelectedItem is not Row row) return;
        var r = MessageBox.Show(this, Loc.T("hist.confirm", row.Hash, row.Message), Loc.T("hist.confirmTitle"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        try
        {
            IsEnabled = false;
            StatusText.Text = Loc.T("hist.restoring");
            await _git.RestoreAsync(_project, row.Hash, new Progress<string>(_log));
            Restored = true;
            await LoadAsync();
            StatusText.Text += Loc.T("hist.restored", row.Hash);
        }
        catch (Exception ex)
        {
            _log(Loc.T("hist.error", row.Hash, ex.Message));
            MessageBox.Show(this, ex.Message, Loc.T("hist.errorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }
}
