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

    public NewProjectWindow(ProjectService projects)
    {
        _projects = projects;
        InitializeComponent();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = Loc.T("np.dialogTitle"), Filter = Loc.T("np.filter") };
        if (dlg.ShowDialog(this) != true) return;
        RomBox.Text = dlg.FileName;
        try
        {
            _header = NdsRomHeader.Read(dlg.FileName);
            var ok = _header.Game != BaseGame.Unknown;
            DetectedText.Text = ok
                ? Loc.T("np.detected", _header.Game, _header.GameCode, _header.Region, _header.Generation, _header.Title)
                : Loc.T("np.unknown", _header.GameCode, _header.Title); // GEN5
            DetectedText.Foreground = ok ? System.Windows.Media.Brushes.DarkGreen : System.Windows.Media.Brushes.Firebrick;
            if (string.IsNullOrWhiteSpace(NameBox.Text) && ok)
                NameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            CreateButton.IsEnabled = ok;
        }
        catch (Exception ex)
        {
            _header = null;
            DetectedText.Text = Loc.T("np.readError", ex.Message);
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
            MessageBox.Show(this, ex.Message, Loc.T("np.createError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
