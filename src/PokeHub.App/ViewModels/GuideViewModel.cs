using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Help;
using PokeHub.Core.Localization;

namespace PokeHub.App.ViewModels;

/// <summary>Guia da seção Ajuda, no idioma atual. Recriado quando o idioma muda.</summary>
public sealed class GuideViewModel(Guide guide)
{
    public Guide Guide { get; } = guide;
    public string Id => Guide.Id;
    public string Group => Guide.Group;
    public string GroupTitle => Loc.T("help.group." + Guide.Group);
    public string Title => Guide.Title.Value;
    public string Intro => Guide.Intro?.Value ?? "";
    public IReadOnlyList<GuideStep> Steps { get; } = guide.Steps.Select((s, i) => new GuideStep(i + 1, s.Value)).ToList();
    public IReadOnlyList<string> Tips { get; } = guide.Tips.Select(t => t.Value).ToList();
    public IReadOnlyList<GuideLinkViewModel> Links { get; } = guide.Links.Select(l => new GuideLinkViewModel(l.Label.Value, l.Url)).ToList();
    public bool HasTips => Tips.Count > 0;
    public bool HasLinks => Links.Count > 0;
}

public sealed record GuideStep(int Number, string Text);

public sealed partial class GuideLinkViewModel(string label, string url)
{
    public string Label { get; } = label;
    public string Url { get; } = url;

    [RelayCommand]
    private void Open() => Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
}
