using System.IO;
using System.Windows;
using PokeHub.App.ViewModels;
using PokeHub.App.Views;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;
using PokeHub.Core.Plugins;

namespace PokeHub.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Loc.Instance.Language = Loc.Resolve(HubBootstrapper.SavedLanguage());

        // o tema entra em cada janela, como na principal: no nível do aplicativo, o estilo de TextBlock invadiria os modelos dos controles
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(ApplyTheme));

        var window = new MainWindow();
        MainWindow = window;
        window.RootChosen += root => window.DataContext = CreateViewModel(root);
        window.Show();

        var configured = HubBootstrapper.FindConfiguredRoot();
        if (configured is not null) window.DataContext = CreateViewModel(configured);
        else window.ShowFirstRun();

        // Depuração de layout: PokeHub.exe --screenshot C:\pasta\base.png gera base-home.png, base-tools.png... e fecha.
        var i = Array.IndexOf(e.Args, "--screenshot");
        if (i >= 0 && i + 1 < e.Args.Length && window.DataContext is MainViewModel vm) CaptureSections(window, vm, e.Args[i + 1]);
    }

    private static void ApplyTheme(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window || window is MainWindow || window.Resources.Contains(ThemeMark)) return;
        window.Resources[ThemeMark] = true;
        var theme = new ResourceDictionary { Source = new Uri("pack://application:,,,/PokeHub;component/Views/Theme.xaml") };
        window.Resources.MergedDictionaries.Add(theme);
        window.Background = (System.Windows.Media.Brush)theme["BgBrush"];
        window.Foreground = (System.Windows.Media.Brush)theme["TextBrush"];
    }

    private const string ThemeMark = "PokeHub.ThemeApplied";

    private void CaptureSections(MainWindow window, MainViewModel vm, string basePath)
    {
        window.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(1500); // detecção de shells, git etc.
            foreach (var section in Enum.GetValues<HubSection>())
            {
                vm.Section = section;
                await Task.Delay(300);
                window.UpdateLayout();
                var root = (FrameworkElement)window.Content;
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(root);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                var file = Path.Combine(Path.GetDirectoryName(basePath)!, Path.GetFileNameWithoutExtension(basePath) + "-" + section.ToString().ToLowerInvariant() + ".png");
                using var fs = File.Create(file);
                enc.Save(fs);
            }
            // --screenshot também captura o editor de Pokédex (com mapa) quando o projeto é HGSS
            if (vm.CurrentProject is { } project && Array.IndexOf(Environment.GetCommandLineArgs(), "--editors") >= 0)
            {
                try
                {
                    foreach (var (id, suffix) in new[] { ("pokedex-area-editor", "pokedex"), ("event-map", "events"), ("trainer-editor", "trainers"), ("encounter-editor", "encounters"), ("text-editor", "text") })
                    {
                        BuiltinEditors.Open(id, project, _ => { }, _ => Task.CompletedTask);
                        var win = Windows.OfType<Window>().LastOrDefault(w => w != window);
                        if (win is null) continue;
                        if (win.DataContext is PokedexAreaEditorViewModel dexVm) dexVm.SelectedSpecies = 16; // Pidgey
                        if (win.DataContext is TextEditorViewModel textVm) textVm.EditText = "Hey, how are your Pok\u00E9mon?\\nThis second line is written long enough to pass the box.\\f{STRVAR_1 3,0,0} got a new line!";
                        if (win.DataContext is EventMapViewModel eventVm) eventVm.MapSearch = "Route 29";
                        if (win.DataContext is TrainerEditorViewModel trainerVm) trainerVm.Selected = trainerVm.FilteredTrainers.ElementAtOrDefault(20); // Falkner
                        await Task.Delay(500);
                        win.UpdateLayout();
                        var root = win; // a janela inteira: o fundo do tema é dela, não do conteúdo
                        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bmp.Render(root);
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                        using (var fs = File.Create(Path.Combine(Path.GetDirectoryName(basePath)!, Path.GetFileNameWithoutExtension(basePath) + "-" + suffix + ".png"))) enc.Save(fs);
                        win.Close();
                    }
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); }
                foreach (var (command, suffix) in new[] { ((System.Windows.Input.ICommand)vm.OpenStatsCommand, "stats"), (vm.OpenNarcBrowserCommand, "narcs"), (vm.OpenTablesCommand, "tables"), (vm.OpenScriptsCommand, "scripts"), (vm.OpenNewEventCommand, "newevent") })
                {
                    try
                    {
                        command.Execute(null);
                        var win = Windows.OfType<Window>().LastOrDefault(w => w != window);
                        if (win is null) continue;
                        if (win.DataContext is StatsEditorViewModel statsVm) statsVm.Selected = statsVm.Filtered.FirstOrDefault(o => o.Id == 25);
                        if (win.DataContext is NewEventViewModel newEventVm)
                        {
                            newEventVm.MapFilter = "R29";
                            newEventVm.SelectedMap = newEventVm.Maps.FirstOrDefault();
                            newEventVm.PickTile(40, 20);
                            newEventVm.IsTrade = true;
                            newEventVm.SelectedMovement = newEventVm.Movements[2];
                        }
                        await Task.Delay(800);
                        win.UpdateLayout();
                        var root = win; // a janela inteira: o fundo do tema é dela, não do conteúdo
                        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bmp.Render(root);
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                        using (var fs = File.Create(Path.Combine(Path.GetDirectoryName(basePath)!, Path.GetFileNameWithoutExtension(basePath) + "-" + suffix + ".png"))) enc.Save(fs);
                        win.Close();
                    }
                    catch (Exception ex) { Console.Error.WriteLine(ex); }
                }
            }
            Shutdown();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static MainViewModel CreateViewModel(string root)
    {
        var paths = HubBootstrapper.Initialize(root);
        var config = HubConfig.Load(paths.ConfigFile);
        var catalog = PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        return new MainViewModel(paths, config, catalog);
    }
}
