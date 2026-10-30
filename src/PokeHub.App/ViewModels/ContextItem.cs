using PokeHub.Core.Plugins;

namespace PokeHub.App.ViewModels;

public sealed record ContextItem(ToolContext Context, string Title, string Hint)
{
    public static readonly IReadOnlyList<ContextItem> All =
    [
        new(ToolContext.Maps, "Mapas", "Editar mapas, matrizes e cabeçalhos"),
        new(ToolContext.Events, "Eventos", "NPCs, warps, triggers"),
        new(ToolContext.Scripts, "Scripts", "Lógica dos eventos"),
        new(ToolContext.Trainers, "Treinadores", "Times e classes de treinador"),
        new(ToolContext.Pokemon, "Pokémon", "Stats, learnsets, evoluções"),
        new(ToolContext.Text, "Texto", "Diálogos e nomes"),
        new(ToolContext.Graphics, "Gráficos", "Sprites, tiles, paletas"),
        new(ToolContext.Models, "Modelos 3D", "NSBMD, texturas"),
        new(ToolContext.Audio, "Áudio", "SDAT, músicas e efeitos"),
        new(ToolContext.Emulator, "Emulador", "Testar a ROM"),
        new(ToolContext.Decomp, "Decomp", "Projetos de decompilação da pret"),
        new(ToolContext.Utility, "Utilitários", "Exploradores e conversores"),
    ];
}
