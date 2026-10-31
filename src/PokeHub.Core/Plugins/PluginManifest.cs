using PokeHub.Core.Localization;

namespace PokeHub.Core.Plugins;

/// <summary>Contextos da tela principal. Cada plugin declara em quais aparece.</summary>
public enum ToolContext
{
    Maps, Events, Scripts, Graphics, Models, Pokemon, Trainers, Text, Audio, Emulator, Decomp, Utility,
}

/// <summary>Onde a ferramenta é instalada: uma vez no hub (tools/) ou por projeto (projects/x/decomp/).</summary>
public enum PluginScope { Hub, Project }

public enum SourceType
{
    /// <summary>Baixa um asset da última release do GitHub (zip, exe ou jar).</summary>
    GitHubRelease,
    /// <summary>git clone do repositório.</summary>
    GitClone,
    /// <summary>Já instalado na máquina; o usuário aponta o executável.</summary>
    System,
    /// <summary>Editor do próprio hub; nada a instalar. O id do manifesto identifica o editor.</summary>
    Builtin,
    /// <summary>Runtime gerenciado (ver RuntimeManager.Known); source.runtimeId diz qual.</summary>
    Runtime,
}

/// <summary>Descrição de uma ferramenta suportada, carregada de plugins/*.json.</summary>
public sealed class PluginManifest
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public LocalizedText Description { get; set; } = new("");
    /// <summary>Para que serve, quem precisa e o que acontece ao instalar. Mostrado no card em "Para que serve?".</summary>
    public LocalizedText? Purpose { get; set; }
    /// <summary>Custo em disco, download e tempo (ex.: "40 MB de download, 2,3 GB em disco, uns 5 minutos").</summary>
    public LocalizedText? Cost { get; set; }
    /// <summary>true = pedir confirmação antes de instalar, mostrando o custo (instalações pesadas).</summary>
    public bool ConfirmInstall { get; set; }
    public string? Homepage { get; set; }
    public string? License { get; set; }
    public List<ToolContext> Contexts { get; set; } = [];
    /// <summary>Gerações atendidas. GEN5: plugins de Gen 5 declaram [5] e são filtrados pelo projeto aberto.</summary>
    public List<int> Generations { get; set; } = [4];
    public PluginScope Scope { get; set; } = PluginScope.Hub;
    public required PluginSource Source { get; set; }
    public LaunchSpec? Launch { get; set; }
    /// <summary>Para decomps: como compilar e onde sai a ROM.</summary>
    public BuildSpec? Build { get; set; }
    /// <summary>Para runtimes: o que rodar depois de instalar.</summary>
    public SetupSpec? Setup { get; set; }
    /// <summary>Dependências: "java21"/"java17" o hub instala; "unix-shell" exige MSYS2 ou WSL; o resto é informativo.</summary>
    public List<string> Requires { get; set; } = [];
    public LocalizedText? Notes { get; set; }
}

public sealed class PluginSource
{
    public SourceType Type { get; set; }
    /// <summary>"owner/repo" no GitHub.</summary>
    public string? Repo { get; set; }
    /// <summary>Regex aplicada ao nome do asset da release.</summary>
    public string? AssetPattern { get; set; }
    /// <summary>Executável dentro do pacote extraído (procurado recursivamente). Se nulo e o asset não é zip, o próprio asset é o executável.</summary>
    public string? Executable { get; set; }
    public string? Branch { get; set; }
    public bool Recursive { get; set; } = true;
    /// <summary>Para type = runtime: id em RuntimeManager.Known (ex.: "msys2").</summary>
    public string? RuntimeId { get; set; }
}

/// <summary>Preparação após instalar (hoje: pacotes pacman para o MSYS2).</summary>
public sealed class SetupSpec
{
    public List<string> Pacman { get; set; } = [];
    /// <summary>Versão da libpng a compilar estaticamente para o runtime msys (ex.: "1.6.50"); null = não compilar.</summary>
    public string? LibpngVersion { get; set; }
}

public sealed class BuildSpec
{
    /// <summary>Comando executado no shell Unix dentro da pasta do repositório (ex.: "make rom").</summary>
    public string Command { get; set; } = "make";
    /// <summary>Caminho relativo da ROM gerada (ex.: "build/pokeplatinum.us.nds").</summary>
    public required string Output { get; set; }
    /// <summary>Aviso mostrado antes do primeiro build (quando ainda não existe a pasta de build).</summary>
    public LocalizedText? FirstBuildNotice { get; set; }
}

public sealed class LaunchSpec
{
    /// <summary>null = executa o binário direto; "java" = java -jar.</summary>
    public string? Runtime { get; set; }
    /// <summary>Preparação feita pelo hub antes de abrir (ver <see cref="LaunchHooks"/>), ex.: "dspre-open-workspace".</summary>
    public string? Hook { get; set; }
    /// <summary>Argumentos com variáveis: ${project} ${workspace} ${baseRom} ${rom} ${builds} ${decomp} ${tool} ${hub}.</summary>
    public List<string> Args { get; set; } = [];
    public string? WorkingDirectory { get; set; }
}
