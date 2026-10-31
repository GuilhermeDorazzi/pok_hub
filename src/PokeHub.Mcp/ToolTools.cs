using System.ComponentModel;
using ModelContextProtocol.Server;
using PokeHub.Core.Plugins;
using PokeHub.Core.Projects;

namespace PokeHub.Mcp;

/// <summary>Ferramentas externas do catálogo: listar, instalar, abrir e testar no emulador.</summary>
[McpServerToolType]
public static class ToolTools
{
    [McpServerTool(Name = "list_tools"), Description("Lista as ferramentas do catálogo do hub: id, nome, contextos, se está instalada e para que serve.")]
    public static object ListTools(HubContext hub, [Description("Projeto, para ver ferramentas de escopo de projeto (decomps)")] string? project = null)
    {
        var p = project is null ? null : hub.OpenProject(project);
        return hub.Catalog.All.OrderBy(m => m.Name).Select(m =>
        {
            var inst = hub.Installer.GetInstalled(m, p);
            return new
            {
                id = m.Id,
                name = m.Name,
                contexts = m.Contexts.Select(c => c.ToString()).ToList(),
                scope = m.Scope.ToString(),
                source = m.Source.Type.ToString(),
                installed = inst is not null,
                version = inst?.Version,
                requires = m.Requires,
                heavy = m.ConfirmInstall,
                purpose = m.Purpose?.Value,
                cost = m.Cost?.Value,
                canLaunch = m.Launch is not null,
                canBuild = m.Build is not null,
            };
        }).ToList();
    }

    [McpServerTool(Name = "install_tool"), Description("Instala ou atualiza uma ferramenta do catálogo (download real). Itens pesados (heavy=true, como MSYS2 e decomps) exigem confirm=true.")]
    public static async Task<object> InstallTool(HubContext hub, [Description("Id da ferramenta (de list_tools)")] string id,
        [Description("Projeto, obrigatório para ferramentas de escopo de projeto")] string? project = null,
        [Description("Confirma instalação pesada")] bool confirm = false)
    {
        var m = hub.GetManifest(id);
        if (m.Source.Type is SourceType.Builtin) return new { note = "Editor embutido no hub: nada a instalar." };
        if (m.Source.Type is SourceType.System) throw new InvalidOperationException("Esta ferramenta é apontada manualmente pelo usuário no PokeHub (não tem download).");
        if (m.ConfirmInstall && !confirm)
            throw new InvalidOperationException($"Instalação pesada. {m.Purpose?.Value} Custo: {m.Cost?.Value} Repita com confirm=true depois de confirmar com o usuário.");
        var p = project is null ? null : hub.OpenProject(project);
        if (m.Scope == PluginScope.Project && p is null) throw new ArgumentException("Esta ferramenta é instalada por projeto: informe project.");
        var log = new LogCollector();
        var tool = await hub.Installer.InstallAsync(m, p, log);
        return new { id = m.Id, version = tool.Version, path = tool.ExecutablePath, log = log.Lines };
    }

    [McpServerTool(Name = "launch_tool"), Description("Abre uma ferramenta externa já instalada apontando para o projeto (ex.: dspre, melonds, tinke). Devolve o pid.")]
    public static object LaunchTool(HubContext hub, string id, string? project = null)
    {
        var m = hub.GetManifest(id);
        if (m.Launch is null) throw new InvalidOperationException("Ferramenta sem comando de abertura.");
        if (m.Source.Type is SourceType.Builtin) throw new InvalidOperationException("Editor embutido: abra pelo PokeHub, ou use as ferramentas get_/list_ deste servidor.");
        var p = project is null ? null : hub.OpenProject(project);
        var inst = hub.Installer.GetInstalled(m, p) ?? throw new InvalidOperationException("Ferramenta não instalada: use install_tool.");
        var log = new LogCollector();
        var rt = hub.Launcher.Launch(m, inst, p, log);
        return new { pid = rt.Process.Id, log = log.Lines };
    }

    [McpServerTool(Name = "test_rom"), Description("Empacota o workspace e abre a ROM no emulador (padrão: o escolhido no PokeHub; senão melonds).")]
    public static async Task<object> TestRom(HubContext hub, string project, [Description("Id do emulador: melonds ou desmume")] string? emulator = null)
    {
        var p = hub.OpenProject(project);
        var id = emulator ?? hub.Config.PreferredEmulator ?? "melonds";
        var m = hub.GetManifest(id);
        var inst = hub.Installer.GetInstalled(m, p) ?? throw new InvalidOperationException($"Emulador {id} não instalado: use install_tool.");
        var log = new LogCollector();
        if (hub.Git.IsAvailable && WorkspaceGit.IsRepo(p)) await hub.Git.SnapshotAsync(p, "Antes de testar (agente)", log);
        var rom = await hub.Workspace.PackAsync(p, log);
        var rt = hub.Launcher.Launch(m, inst, p, log);
        return new { rom, emulator = m.Name, pid = rt.Process.Id, log = log.Lines };
    }
}
