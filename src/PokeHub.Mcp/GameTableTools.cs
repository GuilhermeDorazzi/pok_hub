using System.ComponentModel;
using ModelContextProtocol.Server;
using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;

namespace PokeHub.Mcp;

/// <summary>As mesmas tabelas do card "Tabelas do jogo", para agentes: golpes, itens, evoluções, lojas, scripts e o resto.</summary>
[McpServerToolType]
public static class GameTableTools
{
    private static (Project Project, GameTable Table) Open(HubContext hub, string project, string table)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        var tables = GameTables.Load(p);
        var t = tables.FirstOrDefault(x => x.Id == table) ?? throw new ArgumentException($"Tabela {table} não existe. Tabelas: {string.Join(", ", tables.Select(x => x.Id))}.");
        return (p, t);
    }

    [McpServerTool(Name = "list_tables"), Description("Lista as tabelas editáveis do jogo com colunas e quantos donos têm (donos: espécies, lojas ou arquivos de script).")]
    public static object ListTables(HubContext hub, string project)
    {
        var p = hub.OpenProject(project);
        return GameTables.Load(p).Select(t => new { t.Id, owners = t.Owners?.Count, t.CanResize, columns = t.Columns.Select(c => new { c.Key, kind = c.Kind.ToString(), c.Min, c.Max }) }).ToList();
    }

    [McpServerTool(Name = "list_table_owners"), Description("Donos de uma tabela, com o índice usado nas outras ferramentas. Filtro opcional por texto.")]
    public static object ListTableOwners(HubContext hub, string project, string table, string? search = null)
    {
        var (_, t) = Open(hub, project, table);
        return (t.Owners ?? []).Select((name, index) => new { index, name })
            .Where(o => string.IsNullOrEmpty(search) || o.name.Contains(search, StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
    }

    [McpServerTool(Name = "get_table_rows"), Description("Linhas de uma tabela com os valores numéricos de cada coluna. Tabelas sem dono usam owner=0. Devolve no máximo 200 linhas por chamada: use search ou skip.")]
    public static object GetTableRows(HubContext hub, string project, string table, int owner = 0, string? search = null, int skip = 0)
    {
        var (_, t) = Open(hub, project, table);
        CheckOwner(t, owner);
        var rows = t.Rows(owner).Select((row, index) => new { index, row })
            .Where(r => string.IsNullOrEmpty(search) || r.row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        return new
        {
            total = rows.Count,
            rows = rows.Skip(skip).Take(200).Select(r => new { r.index, name = r.row.Name, values = t.Columns.ToDictionary(c => c.Key, c => r.row.Get(c)) }).ToList(),
        };
    }

    [McpServerTool(Name = "set_table_cell"), Description("Altera uma célula de uma tabela e grava, com versão salva antes e depois. Valor fora do limite da coluna é recusado.")]
    public static async Task<object> SetTableCell(HubContext hub, string project, string table, [Description("Índice da linha, de get_table_rows")] int row, string column, int value, int owner = 0)
    {
        var (p, t) = Open(hub, project, table);
        CheckOwner(t, owner);
        var rows = t.Rows(owner);
        if (row < 0 || row >= rows.Count) throw new ArgumentException($"Linha {row} não existe (0 a {rows.Count - 1}).");
        var c = t.Columns.FirstOrDefault(x => x.Key == column) ?? throw new ArgumentException($"Coluna {column} não existe. Colunas: {string.Join(", ", t.Columns.Select(x => x.Key))}.");
        if (value < c.Min || value > c.Max) throw new ArgumentException($"{column} vai de {c.Min} a {c.Max}.");

        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de editar {table}", log);
        int before = rows[row].Get(c);
        rows[row].Set(c, value);
        t.Save();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"{table}: {rows[row].Name}, {column} = {value}", log);
        return new { table, name = rows[row].Name, column, before, after = rows[row].Get(c) };
    }

    private static void CheckOwner(GameTable t, int owner)
    {
        int count = t.Owners?.Count ?? 1;
        if (owner < 0 || owner >= count) throw new ArgumentException($"Dono {owner} não existe (0 a {count - 1}).");
    }
}
