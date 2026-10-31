using PokeHub.Core.Batch;
using PokeHub.Core.Patch;
using Xunit;

namespace PokeHub.Core.Tests;

public class BpsAndBatchTests
{
    [Fact]
    public void Bps_roundtrip_reproduces_target()
    {
        var rng = new Random(42);
        var source = new byte[200_000];
        rng.NextBytes(source);
        var target = (byte[])source.Clone();
        // mudanças espalhadas, inclusive no início e no fim, e um alvo maior que a origem
        target[0] ^= 0xFF;
        for (int i = 50_000; i < 50_300; i++) target[i] = (byte)i;
        target[^1] ^= 0x01;
        target = [.. target, .. new byte[1234]];

        var patch = Bps.Create(source, target, "teste");
        Assert.True(patch.Length < target.Length / 10, $"patch grande demais: {patch.Length}");
        var applied = Bps.Apply(source, patch);
        Assert.Equal(target, applied);
    }

    [Fact]
    public void Bps_finds_moved_data()
    {
        // Como uma ROM reempacotada com os arquivos em outra ordem: metades trocadas e um bloco novo no meio.
        var rng = new Random(7);
        var a = new byte[300_000]; rng.NextBytes(a);
        var b = new byte[200_000]; rng.NextBytes(b);
        var inserted = new byte[500]; rng.NextBytes(inserted);
        var source = (byte[])[.. a, .. b];
        var target = (byte[])[.. b, .. inserted, .. a];
        var patch = Bps.Create(source, target);
        Assert.True(patch.Length < 2000, $"patch deveria ser pequeno com dados movidos: {patch.Length}");
        Assert.Equal(target, Bps.Apply(source, patch));
    }

    [Fact]
    public void Bps_rejects_wrong_source_and_corrupt_patch()
    {
        var source = new byte[5000];
        var target = new byte[5000];
        target[100] = 7;
        var patch = Bps.Create(source, target);
        var other = new byte[5000]; other[0] = 1;
        Assert.Throws<InvalidDataException>(() => Bps.Apply(other, patch));
        patch[10] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => Bps.Apply(source, patch));
    }

    [Fact]
    public void Bps_handles_identical_and_shrinking_targets()
    {
        var source = Enumerable.Range(0, 3000).Select(i => (byte)i).ToArray();
        Assert.Equal(source, Bps.Apply(source, Bps.Create(source, source)));
        var shorter = source[..1000];
        Assert.Equal(shorter, Bps.Apply(source, Bps.Create(source, shorter)));
    }

    [Theory]
    [InlineData(10, 150, 0, 1, 100, 15)]
    [InlineData(10, 100, 5, 1, 100, 15)]
    [InlineData(98, 110, 0, 1, 100, 100)]
    [InlineData(3, 50, 0, 5, 100, 5)]
    [InlineData(10, 115, 0, 1, 100, 12)] // 11,5 arredonda para cima
    public void ScaleLevel_applies_percent_offset_and_clamp(int level, double percent, int offset, int min, int max, int expected) =>
        Assert.Equal(expected, BatchEditor.ScaleLevel(level, percent, offset, min, max));
}
