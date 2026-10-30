using System.IO;
using System.Windows;
using Microsoft.Win32;
using PokeHub.Core.Projects;
using PokeHub.Core.Rom;

namespace PokeHub.App.Views;

public partial class NewProjectWindow : Window
{
    private readonly ProjectService _projects;
    private NdsRomHeader? _header;

    public Project? Created { get; private set; }

    public NewProjectWindow(ProjectService projects)
    {
        _projects = projects;
        InitializeComponent();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "ROM base", Filter = "ROM Nintendo DS|*.nds|Todos|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        RomBox.Text = dlg.FileName;
        try
        {
            _header = NdsRomHeader.Read(dlg.FileName);
            var ok = _header.Game != BaseGame.Unknown;
            DetectedText.Text = ok
                ? $"Detectado: {_header.Game} ({_header.GameCode}, {_header.Region}) · Gen {_header.Generation} · {_header.Title}"
                : $"ROM não reconhecida (game code '{_header.GameCode}', título '{_header.Title}'). Suportado: Gen 4."; // GEN5
            DetectedText.Foreground = ok ? System.Windows.Media.Brushes.DarkGreen : System.Windows.Media.Brushes.Firebrick;
            if (string.IsNullOrWhiteSpace(NameBox.Text) && ok)
                NameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            CreateButton.IsEnabled = ok;
        }
        catch (Exception ex)
        {
            _header = null;
            DetectedText.Text = $"Erro ao ler a ROM: {ex.Message}";
            DetectedText.Foreground = System.Windows.Media.Brushes.Firebrick;
            CreateButton.IsEnabled = false;
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Created = _projects.Create(NameBox.Text.Trim(), RomBox.Text);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Não foi possível criar o projeto", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
