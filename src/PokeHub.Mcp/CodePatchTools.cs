using System.ComponentModel;
using ModelContextProtocol.Server;
using PokeHub.Core.CodeInjection;

namespace PokeHub.Mcp;

/// <summary>Injeção de código: os patches ficam em workspace/code-patches/&lt;nome&gt;/ (patch.json e fontes), editáveis como arquivos comuns.</summary>
[McpServerToolType]
public static class CodePatchTools
{
    private static object Describe(PatchApplyResult r) => new
    {
        expansionAdded = r.ExpansionAdded,
        freeBytes = r.FreeBytes,
        patches = r.Patches.Select(p => new { p.Name, address = $"0x{p.Address:X8}", p.Size, p.Hooks }).ToList(),
    };

    [McpServerTool(Name = "list_code_patches"), Description("Lista os patches de código do projeto: pasta, descrição, se está ativo, ganchos e onde foi aplicado.")]
    public static object ListCodePatches(HubContext hub, string project)
    {
        var p = hub.OpenProject(project);
        var ledger = CodePatchService.LoadLedger(p);
        return new
        {
            folder = CodePatchService.PatchesDir(p),
            patches = CodePatchService.List(p).Select(x => new
            {
                x.Name, x.Directory, x.Manifest.Description, x.Manifest.Enabled, x.Manifest.GameCodes, hooks = x.Manifest.Hooks,
                applied = ledger.Regions.Where(r => r.Patch == x.Name).Select(r => new { address = $"0x{r.Address:X8}", r.Size }).FirstOrDefault(),
            }).ToList(),
        };
    }

    [McpServerTool(Name = "create_code_patch"), Description("Cria um patch de código inativo com patch.json e main.c de modelo. Edite os arquivos, ponha enabled=true e chame apply_code_patches.")]
    public static object CreateCodePatch(HubContext hub, string project, [Description("Nome do patch (vira o nome da pasta)")] string name)
    {
        var patch = CodePatchService.Create(hub.OpenProject(project), name);
        return new { patch.Name, patch.Directory };
    }

    [McpServerTool(Name = "apply_code_patches"), Description("Compila os patches ativos e os grava no workspace, substituindo a aplicação anterior. Em erro de compilação ou checagem, nada é gravado; o log traz a mensagem do compilador. Salva uma versão antes e depois, quando há git.")]
    public static Task<object> ApplyCodePatches(HubContext hub, string project) =>
        RunAsync(hub, project, "Antes de aplicar patches de código", "Patches de código aplicados", (s, p, log) => s.ApplyAsync(p, log));

    [McpServerTool(Name = "revert_code_patches"), Description("Tira do workspace tudo o que os patches de código gravaram. As pastas dos patches e a expansão do ARM9 ficam. Salva uma versão antes e depois, quando há git.")]
    public static Task<object> RevertCodePatches(HubContext hub, string project) =>
        RunAsync(hub, project, "Antes de remover patches de código", "Patches de código removidos", (s, p, log) => s.RevertAsync(p, log));

    private static async Task<object> RunAsync(HubContext hub, string project, string before, string after,
        Func<CodePatchService, PokeHub.Core.Projects.Project, IProgress<string>, Task<PatchApplyResult>> action)
    {
        var p = hub.OpenProject(project);
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, before, log);
        try
        {
            var result = await action(new CodePatchService(hub.Paths), p, log);
            if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, after, log);
            return new { result = Describe(result), log = log.Lines };
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or NotSupportedException)
        {
            return new { error = ex.Message, log = log.Lines };
        }
    }
}
