using PokeHub.Core.Help;
using PokeHub.Core.Localization;
using PokeHub.Core.Plugins;
using Xunit;

namespace PokeHub.Core.Tests;

public class GuideTests
{
    private static GuideCatalog Load() => GuideCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "guides"));

    [Fact]
    public void Guides_load_with_both_languages_and_valid_groups()
    {
        var catalog = Load();
        Assert.True(catalog.All.Count >= 10);
        foreach (var g in catalog.All)
        {
            Assert.Contains(g.Group, GuideCatalog.GroupOrder);
            Assert.NotEmpty(g.Steps);
            foreach (var t in new[] { g.Title, g.Intro! }.Concat(g.Steps).Concat(g.Tips).Concat(g.Links.Select(l => l.Label)))
            {
                Assert.True(t.Variants.ContainsKey("pt-BR"), $"{g.Id}: texto sem pt-BR");
                Assert.True(t.Variants.ContainsKey("en-US"), $"{g.Id}: texto sem en-US");
            }
        }
        Assert.Equal(catalog.All.Count, catalog.All.Select(g => g.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Guide_tools_and_features_point_to_real_plugins()
    {
        var guides = Load();
        var plugins = PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        foreach (var g in guides.All)
            foreach (var id in g.Tools)
                Assert.NotNull(plugins.Get(id));
        // cada ferramenta instalável deve ter um guia
        foreach (var m in plugins.All.Where(m => m.Source.Type != SourceType.Builtin))
            Assert.NotNull(guides.ForTool(m.Id));
        Assert.Equal("maps", guides.ForTool("dspre")!.Feature);
        Assert.NotNull(guides.ForFeature("emulator"));
    }

    [Fact]
    public void Guide_text_follows_language()
    {
        var g = Load().Get("getting-started")!;
        var original = Loc.Instance.Language;
        try
        {
            Loc.Instance.Language = "en-US";
            Assert.StartsWith("Getting started", g.Title.Value);
            Loc.Instance.Language = "pt-BR";
            Assert.StartsWith("Primeiros passos", g.Title.Value);
        }
        finally { Loc.Instance.Language = original; }
    }
}
