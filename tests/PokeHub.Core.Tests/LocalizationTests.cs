using System.Text.Json;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;
using PokeHub.Core.Plugins;
using Xunit;

namespace PokeHub.Core.Tests;

public class LocalizationTests
{
    [Fact]
    public void Both_languages_have_the_same_keys()
    {
        var pt = Loc.Instance.Keys("pt-BR").ToHashSet();
        var en = Loc.Instance.Keys("en-US").ToHashSet();
        Assert.NotEmpty(pt);
        Assert.Empty(pt.Except(en));
        Assert.Empty(en.Except(pt));
    }

    [Fact]
    public void Switching_language_changes_texts_and_falls_back()
    {
        var original = Loc.Instance.Language;
        try
        {
            Loc.Instance.Language = "pt-BR";
            Assert.Equal("Novo projeto", Loc.T("main.newProject"));
            Loc.Instance.Language = "en-US";
            Assert.Equal("New project", Loc.T("main.newProject"));
            Assert.Equal("Extracting x...", Loc.T("vm.extracting", "x"));
            Assert.Equal("[chave.inexistente]", Loc.T("chave.inexistente"));
            Loc.Instance.Language = "xx-XX"; // desconhecido: volta ao padrão
            Assert.Equal(Loc.Default, Loc.Instance.Language);
        }
        finally { Loc.Instance.Language = original; }
    }

    [Fact]
    public void Localized_text_accepts_string_or_per_language_object()
    {
        var json = """{"a": "só texto", "b": {"pt-BR": "oi", "en-US": "hi"}, "c": {"en-US": "only en"}}""";
        var doc = JsonSerializer.Deserialize<Dictionary<string, LocalizedText>>(json, HubConfig.JsonOptions)!;
        var original = Loc.Instance.Language;
        try
        {
            Loc.Instance.Language = "en-US";
            Assert.Equal("só texto", doc["a"].Value);
            Assert.Equal("hi", doc["b"].Value);
            Assert.Equal("only en", doc["c"].Value);
            Loc.Instance.Language = "pt-BR";
            Assert.Equal("oi", doc["b"].Value);
            Assert.Equal("only en", doc["c"].Value); // sem pt-BR: primeira variante
        }
        finally { Loc.Instance.Language = original; }
    }

    [Fact]
    public void Manifests_carry_english_variants()
    {
        var catalog = PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        foreach (var m in catalog.All)
        {
            Assert.True(m.Purpose!.Variants.ContainsKey("en-US"), $"{m.Id} purpose sem en-US");
            Assert.True(m.Description.Variants.ContainsKey("en-US"), $"{m.Id} description sem en-US");
        }
    }
}
