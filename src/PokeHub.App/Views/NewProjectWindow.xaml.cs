using System.IO;
using System.Windows;
using Microsoft.Win32;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;
using PokeHub.Core.Rom;

namespace PokeHub.App.Views;

public partial class NewProjectWindow : Window
{
    private readonly ProjectService _projects;
    private NdsRomHeader? _header;

    public Project? Created { get; private set; }

    public NewProjectWindow(ProjectService projects, string? romPath = null)
    {
        _projects = projects;
        InitializeComponent();
        if (romPath is not null) Loaded += (_, _) => UseRom(romPath);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = Loc.T("np.dialogTitle"), Filter = Loc.T("np.filter") };
        if (dlg.ShowDialog(this) != true) return;
        UseRom(dlg.FileName);
    }

    /// <summary>Preenche a janela com uma ROM (escolhida no diálogo ou vinda de um patch aplicado).</summary>
    private void UseRom(string fileName)
    {
        RomBox.Text = fileName;
        try
        {
            _header = NdsRomHeader.Read(fileName);
            var ok = _header.Game != BaseGame.Unknown;
            DetectedText.Text = ok
                ? Loc.T("np.detected", _header.Game, _header.GameCode, _header.Region, _header.Generation, _header.Title)
                : Loc.T("np.unknown", _header.GameCode, _header.Title); // GEN5
            DetectedText.Foreground = ok ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Salmon;
            if (string.IsNullOrWhiteSpace(NameBox.Text) && ok)
                NameBox.Text = Path.GetFileNameWithoutExtension(fileName);
            CreateButton.IsEnabled = ok;
        }
        catch (Exception ex)
        {
            _header = null;
            DetectedText.Text = Loc.T("np.readError", ex.Message);
            DetectedText.Foreground = System.Windows.Media.Brushes.Salmon;
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
            MessageBox.Show(this, ex.Message, Loc.T("np.createError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
