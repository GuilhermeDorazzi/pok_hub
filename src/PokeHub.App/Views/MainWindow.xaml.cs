using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PokeHub.App.ViewModels;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;

namespace PokeHub.App.Views;

public partial class MainWindow : Window
{
    private GridLength _logHeight = new(180);

    /// <summary>Disparado na primeira execução quando o usuário escolhe a pasta raiz do hub.</summary>
    public event Action<string>? RootChosen;

    public MainWindow()
    {
        InitializeComponent();
        FirstRunLanguage.ItemsSource = LanguageOption.All;
        FirstRunLanguage.SelectedItem = LanguageOption.All.FirstOrDefault(l => l.Code == Loc.Instance.Language);
    }

    public void ShowFirstRun()
    {
        FirstRunOverlay.Visibility = Visibility.Visible;
        MainContent.IsEnabled = false;
    }

    private void FirstRunLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FirstRunLanguage.SelectedItem is LanguageOption l && l.Code != Loc.Instance.Language)
        {
            Loc.Instance.Language = l.Code;
            HubBootstrapper.SaveLanguage(l.Code);
        }
    }

    private void ChooseRoot_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("firstRun.dialogTitle"), Multiselect = false };
        if (dlg.ShowDialog(this) != true) return;
        FirstRunOverlay.Visibility = Visibility.Collapsed;
        MainContent.IsEnabled = true;
        RootChosen?.Invoke(dlg.FolderName);
    }

    private void Log_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.ScrollToEnd();
    }

    private void ToggleLog_Click(object sender, RoutedEventArgs e)
    {
        bool hidden = LogBody.Visibility == Visibility.Collapsed;
        if (hidden)
        {
            LogBody.Visibility = Visibility.Visible;
            LogSplitter.Visibility = Visibility.Visible;
            LogRow.Height = _logHeight;
            LogRow.MinHeight = 80;
            ToggleLogButton.Content = Loc.T("main.hide");
        }
        else
        {
            _logHeight = LogRow.Height;
            LogBody.Visibility = Visibility.Collapsed;
            LogSplitter.Visibility = Visibility.Collapsed;
            LogRow.MinHeight = 0;
            LogRow.Height = GridLength.Auto;
            ToggleLogButton.Content = Loc.T("main.show");
        }
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        var text = (DataContext as MainViewModel)?.LogText ?? "";
        if (text.Length == 0) return;
        try
        {
            Clipboard.SetText(text);
            CopyLogButton.Content = Loc.T("main.copied");
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) => { CopyLogButton.Content = Loc.T("main.copyAll"); timer.Stop(); };
            timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("main.copyError"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
