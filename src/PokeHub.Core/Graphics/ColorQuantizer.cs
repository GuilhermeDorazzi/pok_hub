namespace PokeHub.Core.Graphics;

/// <summary>Reduz uma imagem às cores que o DS mostra, com corte pela mediana. O índice 0 fica para o transparente.</summary>
public static class ColorQuantizer
{
    /// <summary>Paleta sem o transparente e um índice por pixel: 0 é transparente, e o resto aponta para a paleta a partir de 1.</summary>
    public sealed record Result(ushort[] Palette, byte[] Indices, int OriginalColors);

    /// <summary>Cor de 15 bits do DS, a partir de 8 bits por canal.</summary>
    public static ushort ToDs(byte r, byte g, byte b) => (ushort)((r >> 3) | ((g >> 3) << 5) | ((b >> 3) << 10));

    public static Result Reduce(RgbaImage image, int maxColors)
    {
        var counts = new Dictionary<ushort, int>();
        var pixels = new ushort?[image.Width * image.Height];
        for (int i = 0; i < pixels.Length; i++)
        {
            int o = i * 4;
            if (image.Pixels[o + 3] < 128) continue;
            var color = ToDs(image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2]);
            pixels[i] = color;
            counts[color] = counts.GetValueOrDefault(color) + 1;
        }

        var palette = counts.Count <= maxColors
            ? counts.Keys.OrderBy(c => c).ToArray()
            : MedianCut(counts, maxColors);

        var nearest = new Dictionary<ushort, byte>();
        byte Nearest(ushort color)
        {
            if (nearest.TryGetValue(color, out var found)) return found;
            int best = 0, bestDistance = int.MaxValue;
            for (int p = 0; p < palette.Length; p++)
            {
                int d = Distance(color, palette[p]);
                if (d < bestDistance) { bestDistance = d; best = p; }
            }
            return nearest[color] = (byte)(best + 1);
        }

        var indices = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            if (pixels[i] is { } color) indices[i] = Nearest(color);
        return new Result(palette, indices, counts.Count);
    }

    private static int Channel(ushort color, int channel) => (color >> (channel * 5)) & 31;

    private static int Distance(ushort a, ushort b)
    {
        int total = 0;
        for (int channel = 0; channel < 3; channel++)
        {
            int d = Channel(a, channel) - Channel(b, channel);
            total += d * d;
        }
        return total;
    }

    private static ushort[] MedianCut(Dictionary<ushort, int> counts, int maxColors)
    {
        var boxes = new List<List<ushort>> { counts.Keys.ToList() };
        while (boxes.Count < maxColors)
        {
            // corta a caixa com a maior variação de cor, ponderada por quantos pixels ela cobre
            int pick = -1, pickChannel = 0;
            long pickScore = -1;
            for (int b = 0; b < boxes.Count; b++)
            {
                if (boxes[b].Count < 2) continue;
                for (int channel = 0; channel < 3; channel++)
                {
                    int ch = channel;
                    int range = boxes[b].Max(c => Channel(c, ch)) - boxes[b].Min(c => Channel(c, ch));
                    long score = range * (long)boxes[b].Sum(c => counts[c]);
                    if (range > 0 && score > pickScore) { pickScore = score; pick = b; pickChannel = channel; }
                }
            }
            if (pick < 0) break;

            var sorted = boxes[pick].OrderBy(c => Channel(c, pickChannel)).ToList();
            long half = sorted.Sum(c => counts[c]) / 2, running = 0;
            int cut = 1;
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                running += counts[sorted[i]];
                cut = i + 1;
                if (running >= half) break;
            }
            boxes[pick] = sorted[..cut];
            boxes.Add(sorted[cut..]);
        }

        return [.. boxes.Select(box =>
        {
            long total = box.Sum(c => counts[c]);
            int Mean(int channel) => (int)(box.Sum(c => (long)Channel(c, channel) * counts[c]) / total);
            return (ushort)(Mean(0) | (Mean(1) << 5) | (Mean(2) << 10));
        }).Distinct()];
    }
}
