using System.Windows;
using System.Windows.Controls;
using PokeHub.Core.Projects;

namespace PokeHub.App.Views;

public partial class HistoryWindow : Window
{
    private readonly WorkspaceGit _git;
    private readonly Project _project;
    private readonly Action<string> _log;

    private sealed record Row(string Hash, string Message);

    /// <summary>true se alguma versão foi restaurada enquanto a janela esteve aberta.</summary>
    public bool Restored { get; private set; }

    public HistoryWindow(WorkspaceGit git, Project project, Action<string> log)
    {
        _git = git;
        _project = project;
        _log = log;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        StatusText.Text = "Carregando...";
        var history = await _git.HistoryAsync(_project, 100);
        VersionsList.ItemsSource = history.Select(h => new Row(h.Hash, h.Message)).ToList();
        StatusText.Text = history.Count == 0 ? "Nenhuma versão ainda." : $"{history.Count} versões (mais recente primeiro).";
    }

    private void VersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RestoreButton.IsEnabled = VersionsList.SelectedItem is Row && VersionsList.SelectedIndex > 0;

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (VersionsList.SelectedItem is not Row row) return;
        var r = MessageBox.Show(this,
            $"Voltar o workspace para a versão {row.Hash}?\n\n\"{row.Message}\"\n\n" +
            "Feche as ferramentas que estiverem com o workspace aberto antes de continuar, senão elas podem sobrescrever os arquivos restaurados.",
            "Restaurar versão", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;

        try
        {
            IsEnabled = false;
            StatusText.Text = "Restaurando...";
            await _git.RestoreAsync(_project, row.Hash, new Progress<string>(_log));
            Restored = true;
            await LoadAsync();
            StatusText.Text += $"  Restaurado de {row.Hash}.";
        }
        catch (Exception ex)
        {
            _log($"ERRO ao restaurar {row.Hash}: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Falha ao restaurar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }
}
