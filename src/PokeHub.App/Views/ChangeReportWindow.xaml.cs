using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PokeHub.App.ViewModels;
using PokeHub.Core.Localization;
using PokeHub.Core.Reports;

namespace PokeHub.App.Views;

/// <summary>Uma mudança em colunas: onde, antes e depois. O que não tem "antes → depois" fica só em Onde.</summary>
public sealed record ChangeRow(string Category, string Where, string Before, string After, BitmapSource? BeforeSprite, BitmapSource? AfterSprite)
{
    public string Arrow => After.Length > 0 ? "→" : "";
    public string FullText => After.Length > 0 ? $"{Where}: {Before} → {After}" : Where;
}

/// <summary>Mostra o relatório "o que mudou" em colunas de antes e depois, com sprite quando o texto cita um Pokémon, filtro por categoria e por texto.</summary>
public partial class ChangeReportWindow : Window
{
    private static readonly string[] Order = ["trainers", "encounters", "texts", "files"];
    private sealed record CategoryOption(string? Id, string Label);

    private readonly List<ChangeRow> _rows = [];

    /// <param name="catalog">Espécies do jogo, para as sprites; sem ele, o relatório sai só em texto.</param>
    public ChangeReportWindow(string intro, IReadOnlyList<ChangeLine> lines, SpeciesCatalog? catalog = null)
    {
        InitializeComponent();
        Intro.Text = intro;
        var ids = catalog?.Options.Where(o => o.Id > 0 && o.Name.Length > 2).OrderByDescending(o => o.Name.Length).ToList();
        BitmapSource? SpriteIn(string text)
        {
            var hit = ids?.FirstOrDefault(o => text.Contains(o.Name, StringComparison.Ordinal));
            return hit is null ? null : catalog!.Sprite(hit.Id);
        }

        var categories = Order.Concat(lines.Select(l => l.Category).Distinct().Except(Order)).Where(c => lines.Any(l => l.Category == c)).ToList();
        foreach (var cat in categories)
            foreach (var line in lines.Where(l => l.Category == cat))
            {
                var (where, before, after) = Split(line.Text);
                _rows.Add(new ChangeRow(cat, where, before, after, SpriteIn(before), SpriteIn(after)));
            }

        var options = new List<CategoryOption> { new(null, Loc.T("report.allCategories", lines.Count)) };
        options.AddRange(categories.Select(c => new CategoryOption(c, $"{Loc.T("report.cat." + c)} ({lines.Count(l => l.Category == c)})")));
        CategoryFilter.ItemsSource = options;
        CategoryFilter.SelectedIndex = 0;
        Summary.Text = Loc.T("report.summary", lines.Count);
        if (lines.Count == 0) Summary.Text = Loc.T("report.none");
    }

    /// <summary>"Onde: antes → depois", com a seta que os textos do relatório usam.</summary>
    public static (string Where, string Before, string After) Split(string text)
    {
        int arrow = text.IndexOf(" → ", StringComparison.Ordinal);
        if (arrow < 0) return (text, "", "");
        int colon = text.LastIndexOf(": ", arrow, StringComparison.Ordinal);
        if (colon < 0) return ("", text[..arrow], text[(arrow + 3)..]);
        return (text[..colon], text[(colon + 2)..arrow], text[(arrow + 3)..]);
    }

    private IEnumerable<ChangeRow> Visible()
    {
        var category = (CategoryFilter.SelectedItem as CategoryOption)?.Id;
        var query = TextFilter.Text.Trim();
        return _rows.Where(r => (category is null || r.Category == category)
            && (query.Length == 0 || r.FullText.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        var visible = Visible().ToList();
        Rows.ItemsSource = visible;
        Summary.Text = _rows.Count == 0 ? Loc.T("report.none") : Loc.T("report.shown", visible.Count, _rows.Count);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        foreach (var group in Visible().GroupBy(r => r.Category))
        {
            sb.AppendLine($"== {Loc.T("report.cat." + group.Key)} ({group.Count()}) ==");
            foreach (var r in group) sb.AppendLine("  " + r.FullText);
            sb.AppendLine();
        }
        try { Clipboard.SetText(sb.ToString()); CopyButton.Content = Loc.T("main.copied"); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Loc.T("main.copyError"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
