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
}

/// <summary>Descrição de uma ferramenta suportada, carregada de plugins/*.json.</summary>
public sealed class PluginManifest
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public string? Homepage { get; set; }
    public string? License { get; set; }
    public List<ToolContext> Contexts { get; set; } = [];
    /// <summary>Gerações atendidas. GEN5: plugins de Gen 5 declaram [5] e são filtrados pelo projeto aberto.</summary>
    public List<int> Generations { get; set; } = [4];
    public PluginScope Scope { get; set; } = PluginScope.Hub;
    public required PluginSource Source { get; set; }
    public LaunchSpec? Launch { get; set; }
    /// <summary>Dependências informativas por enquanto (ex.: "java21", "wsl2", "mwcc").</summary>
    public List<string> Requires { get; set; } = [];
    public string? Notes { get; set; }
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
}

public sealed class LaunchSpec
{
    /// <summary>null = executa o binário direto; "java" = java -jar.</summary>
    public string? Runtime { get; set; }
    /// <summary>Argumentos com variáveis: ${project} ${workspace} ${baseRom} ${rom} ${builds} ${decomp} ${tool} ${hub}.</summary>
    public List<string> Args { get; set; } = [];
    public string? WorkingDirectory { get; set; }
}
