using System.IO;
using System.Windows;
using Microsoft.Win32;
using PokeHub.App.ViewModels;
using PokeHub.App.Views;
using PokeHub.Core.Hub;
using PokeHub.Core.Plugins;

namespace PokeHub.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var root = HubBootstrapper.FindConfiguredRoot() ?? AskForRoot();
        if (root is null) { Shutdown(); return; }

        var paths = HubBootstrapper.Initialize(root);
        var config = HubConfig.Load(paths.ConfigFile);
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        var catalog = PluginCatalog.LoadFromDirectory(pluginsDir);

        var vm = new MainViewModel(paths, config, catalog);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    private static string? AskForRoot()
    {
        MessageBox.Show(
            "Bem-vindo ao PokeHub.\n\nEscolha a pasta onde o hub vai guardar as ferramentas e os projetos.\nSugestão: uma pasta vazia, por exemplo D:\\PokeHub.",
            "Primeira execução", MessageBoxButton.OK, MessageBoxImage.Information);
        var dlg = new OpenFolderDialog { Title = "Pasta raiz do PokeHub", Multiselect = false };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }
}
