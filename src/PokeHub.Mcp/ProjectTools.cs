using System.ComponentModel;
using ModelContextProtocol.Server;
using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;

namespace PokeHub.Mcp;

[McpServerToolType]
public static class ProjectTools
{
    public sealed record ProjectSummary(string Name, string BaseGame, string GameCode, int Generation, string Workspace, bool Extracted, string Folder, bool HasHistory);

    private static ProjectSummary Summarize(Project p) => new(
        p.Name, p.Config.BaseGame.ToString(), p.Config.GameCode, p.Config.Generation,
        WorkspaceService.FormatLabel(p), WorkspaceService.IsExtracted(p), p.Paths.Dir, WorkspaceGit.IsRepo(p));

    [McpServerTool(Name = "hub_info"), Description("Informações do hub: pasta raiz, idioma, projetos, ferramentas e runtimes instalados.")]
    public static object HubInfo(HubContext hub) => new
    {
        root = hub.Paths.Root,
        language = PokeHub.Core.Localization.Loc.Instance.Language,
        projects = hub.Projects.ListProjects().Select(p => p.Name).ToList(),
        installedTools = hub.Config.InstalledTools.ToDictionary(k => k.Key, k => k.Value.Version),
        installedRuntimes = hub.Config.InstalledRuntimes.ToDictionary(k => k.Key, k => k.Value.Version),
        preferredEmulator = hub.Config.PreferredEmulator,
        gitAvailable = hub.Git.IsAvailable,
    };

    [McpServerTool(Name = "list_projects"), Description("Lista os projetos do hub (nome, jogo base, geração, estado do workspace).")]
    public static List<ProjectSummary> ListProjects(HubContext hub) => hub.Projects.ListProjects().Select(Summarize).ToList();

    [McpServerTool(Name = "project_info"), Description("Detalhes de um projeto pelo nome.")]
    public static ProjectSummary ProjectInfo(HubContext hub, [Description("Nome do projeto")] string project) => Summarize(hub.OpenProject(project));

    [McpServerTool(Name = "create_project"), Description("Cria um projeto a partir de uma ROM .nds (Gen 4: Diamond, Pearl, Platinum, HeartGold, SoulSilver) e extrai o workspace.")]
    public static async Task<object> CreateProject(HubContext hub,
        [Description("Nome do projeto (vira o nome da pasta)")] string name,
        [Description("Caminho completo da ROM base .nds")] string romPath)
    {
        var log = new LogCollector();
        var p = hub.Projects.Create(name, romPath);
        var ok = await hub.Workspace.ExtractAsync(p, log);
        if (ok && hub.Git.IsAvailable) await hub.Git.EnsureRepoAsync(p, log);
        return new { project = Summarize(p), extracted = ok, log = log.Lines };
    }

    [McpServerTool(Name = "extract_rom"), Description("Extrai a ROM base para o workspace do projeto. Se o workspace já existe, exige overwrite=true (as edições atuais são perdidas; salve uma versão antes).")]
    public static async Task<object> ExtractRom(HubContext hub, string project, [Description("Confirma sobrescrever um workspace já extraído")] bool overwrite = false)
    {
        var p = hub.OpenProject(project);
        if (WorkspaceService.IsExtracted(p) && !overwrite)
            throw new InvalidOperationException("O workspace já está extraído. Passe overwrite=true para substituir (salve uma versão antes com save_version).");
        var log = new LogCollector();
        var ok = await hub.Workspace.ExtractAsync(p, log, overwrite: true);
        if (ok && hub.Git.IsAvailable) await hub.Git.EnsureRepoAsync(p, log);
        return new { extracted = ok, log = log.Lines };
    }

    [McpServerTool(Name = "pack_rom"), Description("Empacota o workspace em uma ROM jogável em builds/ e devolve o caminho. Salva uma versão antes, quando há git.")]
    public static async Task<object> PackRom(HubContext hub, string project)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        var log = new LogCollector();
        if (hub.Git.IsAvailable && WorkspaceGit.IsRepo(p)) await hub.Git.SnapshotAsync(p, "Antes de empacotar (agente)", log);
        var output = await hub.Workspace.PackAsync(p, log);
        return new { rom = output, log = log.Lines };
    }

    [McpServerTool(Name = "set_game_version"), Description("Marca a versão do jogo (V1, V2...) no logo da tela de título e no fim do nome do cartão, e salva um commit com ela. Em HG/SS.")]
    public static async Task<object> SetGameVersion(HubContext hub, string project, [Description("V seguido de 1 a 3 números, como V2")] string version)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        GameFaceService.ApplyVersion(p, version);
        var log = new LogCollector();
        var saved = hub.Git.IsAvailable && await hub.Git.SnapshotAsync(p, $"Versão {version.Trim().ToUpperInvariant()}", log);
        return new { version = GameFaceService.CurrentVersion(p), saved, log = log.Lines };
    }

    [McpServerTool(Name = "save_version"), Description("Salva um ponto de restauração do workspace (commit no git interno do projeto). Use antes de qualquer edição.")]
    public static async Task<object> SaveVersion(HubContext hub, string project, [Description("Mensagem curta descrevendo o estado")] string message)
    {
        var p = hub.OpenProject(project);
        if (!hub.Git.IsAvailable) throw new InvalidOperationException("Git não disponível: instale pelo PokeHub (Configurações) ou install_tool.");
        var log = new LogCollector();
        await hub.Git.EnsureRepoAsync(p, log);
        var changed = await hub.Git.SnapshotAsync(p, message, log);
        return new { saved = changed, note = changed ? "Versão salva." : "Nada mudou desde a última versão.", log = log.Lines };
    }

    [McpServerTool(Name = "list_versions"), Description("Lista as versões salvas do workspace (hash e mensagem), da mais recente para a mais antiga.")]
    public static async Task<object> ListVersions(HubContext hub, string project, [Description("Quantidade máxima")] int count = 20)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceGit.IsRepo(p)) return new { versions = Array.Empty<object>(), note = "Projeto sem histórico ainda." };
        var h = await hub.Git.HistoryAsync(p, count);
        return new { versions = h.Select(v => new { hash = v.Hash, message = v.Message }).ToList() };
    }

    [McpServerTool(Name = "restore_version"), Description("Restaura o workspace para uma versão salva. Destrutivo para edições não salvas: exige confirm=true.")]
    public static async Task<object> RestoreVersion(HubContext hub, string project, [Description("Hash da versão (de list_versions)")] string hash,
        [Description("Deve ser true para executar")] bool confirm = false)
    {
        if (!confirm) throw new InvalidOperationException("Restaurar descarta edições não salvas. Repita com confirm=true depois de confirmar com o usuário.");
        var p = hub.OpenProject(project);
        var log = new LogCollector();
        await hub.Git.RestoreAsync(p, hash, log);
        return new { restored = hash, log = log.Lines };
    }
}
