using System.Windows;
using PokeHub.App.ViewModels;
using PokeHub.App.Views;
using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.App;

/// <summary>Editores do próprio hub, abertos pelos manifestos com source.type = builtin.</summary>
public static class BuiltinEditors
{
    public delegate void Opener(Project project, Action<string> log, Func<string, Task> snapshot);

    public static readonly IReadOnlyDictionary<string, Opener> All = new Dictionary<string, Opener>(StringComparer.OrdinalIgnoreCase)
    {
        ["trainer-editor"] = (project, log, snapshot) =>
        {
            var repo = TrainerRepository.Load(project);
            log(Loc.T("be.trainers", repo.Trainers.Count, repo.Species.Count, repo.Items.Count, repo.Moves.Count));
            var vm = new TrainerEditorViewModel(repo, log, snapshot);
            new TrainerEditorWindow(vm) { Owner = Application.Current.MainWindow }.Show();
        },
        ["pokedex-area-editor"] = (project, log, snapshot) =>
        {
            var repo = PokedexAreaRepository.Load(project);
            log(Loc.T("be.dex", PokedexAreaRepository.SpeciesCount, PokedexAreaRepository.NormalLocations.Count, PokedexAreaRepository.SpecialLocations.Count));
            var vm = new PokedexAreaEditorViewModel(repo, log, snapshot);
            new PokedexAreaEditorWindow(vm) { Owner = Application.Current.MainWindow }.Show();
        },
        // GEN5: editores de Gen 5 entram aqui com o mesmo mecanismo
    };

    public static void Open(string id, Project project, Action<string> log, Func<string, Task> snapshot)
    {
        if (!All.TryGetValue(id, out var opener)) throw new NotSupportedException($"Editor embutido desconhecido: {id}");
        opener(project, log, snapshot);
    }
}
