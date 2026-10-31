using PokeHub.Core.Localization;

namespace PokeHub.App.ViewModels;

public sealed record LanguageOption(string Code, string Name)
{
    public static readonly IReadOnlyList<LanguageOption> All = Loc.Available.Select(a => new LanguageOption(a.Code, a.Name)).ToList();
}
