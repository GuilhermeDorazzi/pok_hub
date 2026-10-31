using PokeHub.Core.Localization;
using PokeHub.Core.Plugins;

namespace PokeHub.App.ViewModels;

/// <summary>Contexto da coluna esquerda. Título e dica vêm do dicionário de idioma (chaves ctx.&lt;nome&gt; e ctx.&lt;nome&gt;.hint).</summary>
public sealed record ContextItem(ToolContext Context)
{
    private string Key => "ctx." + Context.ToString().ToLowerInvariant();
    public string Title => Loc.T(Key);
    public string Hint => Loc.T(Key + ".hint");

    public static readonly IReadOnlyList<ToolContext> Order =
    [
        ToolContext.Maps, ToolContext.Events, ToolContext.Scripts, ToolContext.Trainers, ToolContext.Pokemon, ToolContext.Text,
        ToolContext.Graphics, ToolContext.Models, ToolContext.Audio, ToolContext.Emulator, ToolContext.Decomp, ToolContext.Utility,
    ];

    public static IReadOnlyList<ContextItem> Build() => Order.Select(c => new ContextItem(c)).ToList();
}
