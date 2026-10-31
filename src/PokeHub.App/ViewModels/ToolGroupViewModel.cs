using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PokeHub.Core.Localization;
using PokeHub.Core.Plugins;

namespace PokeHub.App.ViewModels;

public enum ToolsFilter { All, Installed, Updates }

public sealed record ToolsFilterOption(ToolsFilter Filter)
{
    public string Name => Loc.T("tools.filter." + Filter.ToString().ToLowerInvariant());
}

/// <summary>Grupo da seção Ferramentas (Editores, Emuladores e utilitários, Decomp). Mostra só os itens que passam no filtro.</summary>
public sealed partial class ToolGroupViewModel : ObservableObject
{
    public string Key { get; }
    public IReadOnlyList<ToolItemViewModel> AllTools { get; }
    public ObservableCollection<ToolItemViewModel> Tools { get; } = [];

    public ToolGroupViewModel(string key, IEnumerable<ToolItemViewModel> tools)
    {
        Key = key;
        AllTools = tools.ToList();
        ApplyFilter(ToolsFilter.All);
    }

    public string Title => Loc.T("tools.group." + Key);
    public bool HasTools => Tools.Count > 0;

    public void ApplyFilter(ToolsFilter filter)
    {
        Tools.Clear();
        foreach (var t in AllTools)
        {
            var show = filter switch
            {
                ToolsFilter.Installed => t.IsInstalled,
                ToolsFilter.Updates => t.UpdateAvailable,
                _ => true,
            };
            if (show) Tools.Add(t);
        }
        OnPropertyChanged(nameof(HasTools));
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>Grupo de um manifesto pelo seu primeiro contexto.</summary>
    public static string GroupOf(PluginManifest m) => m.Contexts.FirstOrDefault() switch
    {
        ToolContext.Emulator or ToolContext.Utility => "emulators",
        ToolContext.Decomp => "decomp",
        _ => "editors",
    };
}
