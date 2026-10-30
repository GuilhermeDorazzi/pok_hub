using System.IO;
using System.Windows;
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
        var window = new MainWindow();
        MainWindow = window;
        window.RootChosen += root => window.DataContext = CreateViewModel(root);
        window.Show();

        var configured = HubBootstrapper.FindConfiguredRoot();
        if (configured is not null) window.DataContext = CreateViewModel(configured);
        else window.ShowFirstRun();
    }

    private static MainViewModel CreateViewModel(string root)
    {
        var paths = HubBootstrapper.Initialize(root);
        var config = HubConfig.Load(paths.ConfigFile);
        var catalog = PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        return new MainViewModel(paths, config, catalog);
    }
}
