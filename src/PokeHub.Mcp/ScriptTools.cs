using System.ComponentModel;
using ModelContextProtocol.Server;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Projects;

namespace PokeHub.Mcp;

/// <summary>Leitura e edição de scripts: inserir e remover comandos, com saltos recalculados.</summary>
[McpServerToolType]
public static class ScriptTools
{
    private static (Project Project, ScriptArchive Archive) Open(HubContext hub, string project, int file)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        var archive = ScriptArchive.Load(p);
        if (file < 0 || file >= archive.Names.Count) throw new ArgumentException($"Arquivo {file} não existe (0 a {archive.Names.Count - 1}).");
        return (p, archive);
    }

    [McpServerTool(Name = "get_script"), Description("Lista os comandos de um arquivo de scripts, com posição e argumentos. Os arquivos estão em list_table_owners, tabela scripts.")]
    public static object GetScript(HubContext hub, string project, int file)
    {
        var (_, archive) = Open(hub, project, file);
        var editor = archive.Open(file);
        return new
        {
            name = archive.Names[file],
            messageBank = archive.MessageBank(file),
            entries = editor.File.EntryOffsets,
            commands = editor.Instructions().Select(i => new
            {
                offset = i.Offset,
                command = i.Name,
                args = i.Args.Select(a => new { a.Type, a.Value }),
                message = i.Arg("message") is { } m ? archive.Message(file, (int)m.Value) : null,
            }).ToList(),
        };
    }

    [McpServerTool(Name = "insert_script_command"), Description("Insere um comando antes da posição indicada e recalcula saltos. Quem saltava para ali passa a executar o novo comando primeiro. Em saltos, o argumento é a posição absoluta do destino. Salva versão antes e depois.")]
    public static async Task<object> InsertScriptCommand(HubContext hub, string project, int file, [Description("Posição de um comando, de get_script")] int before, [Description("Nome do comando, como SetFlag")] string command, long[] args)
    {
        var (p, archive) = Open(hub, project, file);
        int opcode = -1;
        for (int i = 0; i < archive.Table.Commands.Count; i++)
            if (string.Equals(archive.Table.Commands[i].Name, command, StringComparison.OrdinalIgnoreCase)) { opcode = i; break; }
        if (opcode < 0) throw new ArgumentException($"Comando {command} não existe.");
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de editar o script {file}", log);
        archive.Open(file).Insert(before, opcode, args);
        archive.Save();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Script {file}: {command} inserido", log);
        return new { file, inserted = command, at = before };
    }

    [McpServerTool(Name = "remove_script_command"), Description("Remove o comando na posição indicada e recalcula saltos. Quem saltava para ele passa a cair no comando seguinte. Salva versão antes e depois.")]
    public static async Task<object> RemoveScriptCommand(HubContext hub, string project, int file, [Description("Posição de um comando, de get_script")] int offset)
    {
        var (p, archive) = Open(hub, project, file);
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de editar o script {file}", log);
        archive.Open(file).Remove(offset);
        archive.Save();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Script {file}: comando removido em {offset}", log);
        return new { file, removedAt = offset };
    }
}
