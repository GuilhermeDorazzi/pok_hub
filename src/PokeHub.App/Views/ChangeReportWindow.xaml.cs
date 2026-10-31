using System.Text;
using System.Windows;
using PokeHub.Core.Localization;
using PokeHub.Core.Reports;

namespace PokeHub.App.Views;

/// <summary>Mostra o relatório "o que mudou" agrupado por categoria (treinadores, encontros, textos, outros arquivos).</summary>
public partial class ChangeReportWindow : Window
{
    private static readonly string[] Order = ["trainers", "encounters", "texts", "files"];

    public ChangeReportWindow(string intro, IReadOnlyList<ChangeLine> lines)
    {
        InitializeComponent();
        Intro.Text = intro;
        var sb = new StringBuilder();
        foreach (var cat in Order.Concat(lines.Select(l => l.Category).Distinct().Except(Order)))
        {
            var group = lines.Where(l => l.Category == cat).ToList();
            if (group.Count == 0) continue;
            sb.AppendLine($"== {Loc.T("report.cat." + cat)} ({group.Count}) ==");
            foreach (var l in group) sb.AppendLine("  " + l.Text);
            sb.AppendLine();
        }
        Body.Text = lines.Count == 0 ? Loc.T("report.none") : sb.ToString();
        Summary.Text = Loc.T("report.summary", lines.Count);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Body.Text); CopyButton.Content = Loc.T("main.copied"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("main.copyError"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
