using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Batch;

/// <summary>Diz em que espécie um Pokémon já estaria num certo nível, seguindo a tabela de evoluções do projeto.</summary>
public sealed class EvolutionChart(IReadOnlyDictionary<int, IReadOnlyList<(int Method, int Param, int Target)>> table)
{
    // métodos com nível no parâmetro (constants/pokemon.h). O Shedinja fica de fora: ele aparece a mais, sem substituir o Ninjask.
    private static readonly HashSet<int> ByLevel = [4, 8, 9, 10, 11, 12, 13, 22, 23];
    private const int Shedinja = 14, MaxStages = 3;

    public static EvolutionChart Load(Project project)
    {
        var evolutions = GameTables.Load(project).First(t => t.Id == "evolutions");
        var method = evolutions.Columns.First(c => c.Key == "method");
        var param = evolutions.Columns.First(c => c.Key == "param");
        var target = evolutions.Columns.First(c => c.Key == "target");
        var table = new Dictionary<int, IReadOnlyList<(int, int, int)>>();
        for (int o = 0; o < (evolutions.Owners?.Count ?? 0); o++)
            table[o] = evolutions.Rows(o).Select(r => (r.Get(method), r.Get(param), r.Get(target))).Where(r => r.Item1 != 0).ToList();
        return new EvolutionChart(table);
    }

    /// <summary>
    /// A espécie final nesse nível. Quando há mais de um caminho, vale o primeiro da tabela.
    /// Evoluções sem nível (pedra, amizade, troca) acontecem em otherAt; zero deixa essas de fora.
    /// </summary>
    public int Evolve(int species, int level, int otherAt)
    {
        for (int stage = 0; stage < MaxStages; stage++)
        {
            if (!table.TryGetValue(species, out var rows)) break;
            int next = 0;
            foreach (var (method, param, target) in rows)
            {
                if (method == Shedinja) continue;
                bool ready = ByLevel.Contains(method) ? level >= param : otherAt > 0 && level >= otherAt;
                if (!ready) continue;
                next = target;
                break;
            }
            if (next == 0) break;
            species = next;
        }
        return species;
    }
}
