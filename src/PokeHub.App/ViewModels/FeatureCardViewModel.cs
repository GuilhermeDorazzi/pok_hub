using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using PokeHub.Core.Localization;

namespace PokeHub.App.ViewModels;

public enum FeatureGroup { Edit, Advanced }

/// <summary>
/// Card da tela inicial: um recurso do hub (editar treinadores, testar no emulador...) ligado à ferramenta que o realiza.
/// O estado e o botão principal vêm da ferramenta; quando ela não existe para o jogo aberto, o card avisa.
/// </summary>
public sealed partial class FeatureCardViewModel : ObservableObject
{
    private readonly Func<bool>? _customReady;
    private readonly Func<string>? _customAction;

    public string Key { get; }
    public FeatureGroup Group { get; }
    /// <summary>Marcador curto mostrado no ícone do card (ex.: "M" de mapas).</summary>
    public string Glyph { get; }
    public ToolItemViewModel? Tool { get; }
    /// <summary>Comando usado no lugar do da ferramenta (ex.: Testar no emulador).</summary>
    public ICommand? CustomCommand { get; }

    public FeatureCardViewModel(string key, FeatureGroup group, string glyph, ToolItemViewModel? tool,
        ICommand? customCommand = null, Func<bool>? customReady = null, Func<string>? customAction = null)
    {
        Key = key;
        Group = group;
        Glyph = glyph;
        Tool = tool;
        CustomCommand = customCommand;
        _customReady = customReady;
        _customAction = customAction;
        if (tool is not null) tool.PropertyChanged += (_, _) => Refresh();
    }

    public string Title => Loc.T("feature." + Key);
    public string Text => Loc.T("feature." + Key + ".text");
    public string Via => Tool is { IsBuiltin: false } t ? Loc.T("feature.via", t.Name) : "";

    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _actionText = "";
    [ObservableProperty] private bool _hasAction;
    [ObservableProperty] private ICommand? _actionCommand;
    [ObservableProperty] private bool _needsInstall;
    /// <summary>Guia da seção Ajuda para este recurso, quando existe.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasGuide))] private string? _guideId;
    public bool HasGuide => GuideId is not null;

    public void Refresh()
    {
        if (CustomCommand is not null)
        {
            IsReady = _customReady?.Invoke() ?? true;
            StatusText = IsReady ? Loc.T("feature.ready") : Loc.T("tool.status.notInstalled");
            ActionText = _customAction?.Invoke() ?? Loc.T("main.open");
            ActionCommand = CustomCommand;
            HasAction = true;
            NeedsInstall = !IsReady;
        }
        else if (Tool is null)
        {
            IsReady = false;
            StatusText = Loc.T("feature.unavailable");
            ActionText = "";
            ActionCommand = null;
            HasAction = false;
            NeedsInstall = false;
        }
        else
        {
            var t = Tool;
            IsReady = t.IsInstalled && !t.HasMissingDependency && (!t.IsBuiltin || t.CanLaunch);
            NeedsInstall = !t.IsInstalled && t.CanInstallOrUpdate;
            StatusText = t.IsRunning ? Loc.T("feature.running")
                       : t.IsInstalled && t.HasMissingDependency ? Loc.T("feature.missingDependency")
                       : t.IsInstalled && !t.IsBuiltin ? Loc.T("feature.ready")
                       : t.StatusText;
            if (NeedsInstall)
            {
                ActionText = t.InstallButtonText;
                ActionCommand = t.InstallCommand;
            }
            else if (t.HasBuild)
            {
                ActionText = Loc.T("main.buildRom");
                ActionCommand = t.BuildCommand;
            }
            else
            {
                ActionText = Loc.T("main.open");
                ActionCommand = t.LaunchCommand;
            }
            HasAction = true;
        }
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Via));
    }
}
