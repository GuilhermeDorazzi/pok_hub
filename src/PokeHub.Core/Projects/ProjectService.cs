using PokeHub.Core.Hub;
using PokeHub.Core.Rom;

namespace PokeHub.Core.Projects;

public sealed class ProjectService(HubPaths hub)
{
    public IReadOnlyList<Project> ListProjects()
    {
        if (!Directory.Exists(hub.Projects)) return [];
        var list = new List<Project>();
        foreach (var dir in Directory.EnumerateDirectories(hub.Projects))
        {
            var paths = new ProjectPaths(dir);
            if (!File.Exists(paths.ConfigFile)) continue;
            try { list.Add(new Project { Paths = paths, Config = ProjectConfig.Load(paths.ConfigFile) }); }
            catch { /* projeto corrompido: ignorar na listagem */ }
        }
        return list;
    }

    public Project? Open(string name)
    {
        var paths = new ProjectPaths(hub.ProjectDir(name));
        return File.Exists(paths.ConfigFile)
            ? new Project { Paths = paths, Config = ProjectConfig.Load(paths.ConfigFile) }
            : null;
    }

    /// <summary>Cria o projeto copiando o dump do usuário para base.nds. A extração para workspace/ vem na fase 2.</summary>
    public Project Create(string name, string romPath)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Nome de projeto inválido.", nameof(name));
        var paths = new ProjectPaths(hub.ProjectDir(name));
        if (Directory.Exists(paths.Dir)) throw new InvalidOperationException($"Já existe um projeto chamado '{name}'.");

        var header = NdsRomHeader.Read(romPath);
        if (header.Game == BaseGame.Unknown)
            throw new InvalidDataException($"ROM não reconhecida (game code '{header.GameCode}'). Suportado: Gen 4."); // GEN5: ajustar mensagem

        Directory.CreateDirectory(paths.Dir);
        Directory.CreateDirectory(paths.Workspace);
        Directory.CreateDirectory(paths.Builds);
        Directory.CreateDirectory(paths.Decomp);
        File.Copy(romPath, paths.BaseRom, overwrite: false);

        var config = new ProjectConfig
        {
            Name = name,
            BaseGame = header.Game,
            GameCode = header.GameCode,
            Generation = header.Generation,
        };
        config.Save(paths.ConfigFile);
        return new Project { Paths = paths, Config = config };
    }
}
