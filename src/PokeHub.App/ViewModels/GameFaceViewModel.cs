using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PokeHub.Core.Gen4;
using PokeHub.Core.Graphics;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.App.ViewModels;

public sealed partial class GameFaceViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly Action<string> _log;
    private readonly Func<string, Task> _snapshot;

    [ObservableProperty] private ImageSource? _logo;
    [ObservableProperty] private ImageSource? _icon;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _romTitle = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportLogoCommand), nameof(ImportIconCommand), nameof(SaveBannerCommand))]
    private bool _isBusy;

    public bool CanEditLogo { get; }
    public bool CanEditBanner { get; }

    public GameFaceViewModel(Project project, Action<string> log, Func<string, Task> snapshot)
    {
        _project = project;
        _log = log;
        _snapshot = snapshot;
        CanEditLogo = GameFaceService.CanEditLogo(project);
        CanEditBanner = GameFaceService.CanEditBanner(project);
        if (CanEditBanner)
        {
            var text = GameFaceService.ReadBanner(project);
            Name = text.Name;
            Subtitle = text.Subtitle;
            RomTitle = text.RomTitle;
        }
        RefreshImages();
    }

    private void RefreshImages()
    {
        Logo = GameFaceService.CurrentLogo(_project)?.ToBitmap();
        if (!CanEditBanner) return;
        // lê para a memória: o arquivo precisa ficar livre para a próxima troca
        var icon = new BitmapImage();
        icon.BeginInit();
        icon.CacheOption = BitmapCacheOption.OnLoad;
        icon.StreamSource = new MemoryStream(File.ReadAllBytes(GameFaceService.CurrentIconPath(_project)));
        icon.EndInit();
        icon.Freeze();
        Icon = icon;
    }

    private bool CanAct => !IsBusy;

    private static RgbaImage? PickImage(string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = Loc.T("face.pngFilter") };
        if (dialog.ShowDialog() != true) return null;
        var frame = BitmapDecoder.Create(new Uri(dialog.FileName), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var image = new RgbaImage(converted.PixelWidth, converted.PixelHeight);
        var bgra = new byte[image.Width * image.Height * 4];
        converted.CopyPixels(bgra, image.Width * 4, 0);
        for (int i = 0; i < bgra.Length; i += 4)
        {
            image.Pixels[i] = bgra[i + 2];
            image.Pixels[i + 1] = bgra[i + 1];
            image.Pixels[i + 2] = bgra[i];
            image.Pixels[i + 3] = bgra[i + 3];
        }
        return image;
    }

    private async Task RunAsync(string before, Func<string> change)
    {
        try
        {
            IsBusy = true;
            await _snapshot(before);
            Status = change();
            RefreshImages();
            _log(Status);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or IOException)
        {
            Status = ex.Message;
            _log(Loc.T("vm.error", ex.Message));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ImportLogoAsync()
    {
        if (PickImage(Loc.T("face.pickLogo")) is not { } image) return;
        await RunAsync(Loc.T("face.beforeLogo"), () => Loc.T("face.logoDone", GameFaceService.ImportLogo(_project, image)));
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ImportIconAsync()
    {
        if (PickImage(Loc.T("face.pickIcon")) is not { } image) return;
        await RunAsync(Loc.T("face.beforeIcon"), () => Loc.T("face.iconDone", GameFaceService.ImportIcon(_project, image)));
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task SaveBannerAsync() => await RunAsync(Loc.T("face.beforeBanner"), () =>
    {
        GameFaceService.WriteBanner(_project, new GameFaceService.BannerText(Name, Subtitle, RomTitle));
        return Loc.T("face.bannerDone");
    });
}
