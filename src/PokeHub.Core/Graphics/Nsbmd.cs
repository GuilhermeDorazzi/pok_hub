namespace PokeHub.Core.Graphics;

/// <summary>Um vértice já com a escala do modelo: posição no mundo e coordenada de textura em texels.</summary>
public readonly record struct ModelVertex(float X, float Y, float Z, float S, float T, byte R, byte G, byte B);

/// <summary>Triângulo com o material que estava ativo quando foi desenhado.</summary>
public readonly record struct ModelTriangle(ModelVertex A, ModelVertex B, ModelVertex C, int Material);

public sealed class ModelMaterial
{
    public string? Texture { get; set; }
    public string? Palette { get; set; }
    public bool RepeatS { get; set; }
    public bool RepeatT { get; set; }
    public bool FlipS { get; set; }
    public bool FlipT { get; set; }
    /// <summary>0 a 31; sombras dos prédios ficam em torno de 9.</summary>
    public int Alpha { get; set; } = 31;
}

/// <summary>
/// Leitura de modelos NSBMD (bloco MDL0), o suficiente para desenhar mapas vistos de cima: materiais, comandos do desenho e listas de polígonos do GPU do DS.
/// Sem animação e sem esqueleto: cada nó de mapa é um objeto rígido, e só a matriz de cada nó é ignorada, porque os modelos de terreno usam identidade.
/// Layout conforme o apicula (scurest/apicula, docs de nitro).
/// </summary>
public sealed class NsbmdModel
{
    public List<ModelMaterial> Materials { get; } = [];
    public List<ModelTriangle> Triangles { get; } = [];

    /// <summary>null se o arquivo não for um modelo legível: o mapa segue sem aquele pedaço.</summary>
    public static NsbmdModel? TryRead(ReadOnlySpan<byte> file)
    {
        try { return Read(file.ToArray()); }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidDataException) { return null; }
    }

    public static NsbmdModel Read(byte[] d)
    {
        if (d.Length < 0x14 || d[0] != 'B' || d[1] != 'M' || d[2] != 'D' || d[3] != '0') throw new InvalidDataException("BMD0");
        int mdl0 = BitConverter.ToInt32(d, 0x10);
        var models = NitroList.Read(d, mdl0 + 8);
        if (models.Count == 0) throw new InvalidDataException("MDL0");
        int model = mdl0 + BitConverter.ToInt32(d, models.DataAt(0));

        int sbc = model + BitConverter.ToInt32(d, model + 4);
        int materials = model + BitConverter.ToInt32(d, model + 8);
        int meshes = model + BitConverter.ToInt32(d, model + 12);
        float scale = BitConverter.ToInt32(d, model + 0x1C) / 4096f;

        var result = new NsbmdModel();
        result.ReadMaterials(d, materials);

        var meshList = NitroList.Read(d, meshes);
        int current = -1;
        var drawn = new HashSet<int>();
        // comandos do modelo: quais materiais e malhas usar, na ordem de desenho
        for (int p = sbc; p < d.Length;)
        {
            int op = d[p];
            switch (op & 0x1F)
            {
                case 0x00: p += 1; break;
                case 0x01: p = d.Length; break;
                case 0x02: p += 3; break;
                case 0x03: p += 2; break;
                case 0x04: current = d[p + 1]; p += 2; break;
                case 0x05:
                    int mesh = d[p + 1];
                    if (mesh < meshList.Count) result.ReadMesh(d, meshes, meshList.DataAt(mesh), current, scale);
                    drawn.Add(mesh);
                    p += 2;
                    break;
                case 0x06:
                    // nó: índice, pai e flags; as variantes guardam na pilha de matrizes com bytes a mais
                    p += 4 + (op == 0x26 || op == 0x46 ? 1 : 0) + (op == 0x66 ? 2 : 0);
                    break;
                case 0x07: case 0x08: p += 2; break;
                case 0x09:
                    p += 3 + d[p + 2] * 3;
                    break;
                case 0x0B: p += 1; break;
                case 0x0C: case 0x0D: p += 3; break;
                default: p = d.Length; break;
            }
        }
        // modelo sem comando de desenho para alguma malha: desenha assim mesmo com o material corrente
        for (int i = 0; i < meshList.Count; i++)
            if (!drawn.Contains(i)) result.ReadMesh(d, meshes, meshList.DataAt(i), current, scale);
        return result;
    }

    private void ReadMaterials(byte[] d, int at)
    {
        int texPairs = at + BitConverter.ToUInt16(d, at);
        int palPairs = at + BitConverter.ToUInt16(d, at + 2);
        var list = NitroList.Read(d, at + 4);
        for (int i = 0; i < list.Count; i++)
        {
            int m = at + BitConverter.ToInt32(d, list.DataAt(i));
            uint image = BitConverter.ToUInt32(d, m + 0x14);
            Materials.Add(new ModelMaterial
            {
                RepeatS = (image & (1u << 16)) != 0,
                RepeatT = (image & (1u << 17)) != 0,
                FlipS = (image & (1u << 18)) != 0,
                FlipT = (image & (1u << 19)) != 0,
                Alpha = (int)((BitConverter.ToUInt32(d, m + 0x0C) >> 16) & 31),
            });
        }
        ReadPairs(d, at, texPairs, (m, name) => m.Texture = name);
        ReadPairs(d, at, palPairs, (m, name) => m.Palette = name);
    }

    /// <summary>Listas "nome de textura ou paleta: quais materiais usam".</summary>
    private void ReadPairs(byte[] d, int materials, int listAt, Action<ModelMaterial, string> set)
    {
        var pairs = NitroList.Read(d, listAt);
        for (int i = 0; i < pairs.Count; i++)
        {
            int entry = pairs.DataAt(i);
            int offset = BitConverter.ToUInt16(d, entry), count = d[entry + 2];
            var name = pairs.NameAt(i);
            for (int j = 0; j < count; j++)
            {
                int material = d[materials + offset + j];
                if (material < Materials.Count) set(Materials[material], name);
            }
        }
    }

    private void ReadMesh(byte[] d, int meshes, int entry, int material, float scale)
    {
        int mesh = meshes + BitConverter.ToInt32(d, entry);
        int start = mesh + BitConverter.ToInt32(d, mesh + 8);
        int size = BitConverter.ToInt32(d, mesh + 12);
        new DisplayList(this, material, scale).Run(d, start, size);
    }

    /// <summary>Interpreta os comandos do GPU: vértices, cor, coordenadas de textura e tipo de primitiva.</summary>
    private sealed class DisplayList(NsbmdModel model, int material, float scale)
    {
        private static readonly int[] Params = BuildParams();
        private readonly List<ModelVertex> _strip = [];
        private int _x, _y, _z, _primitive = -1;
        private float _s, _t;
        private byte _r = 255, _g = 255, _b = 255;

        private static int[] BuildParams()
        {
            var p = new int[256];
            p[0x10] = 1; p[0x12] = 1; p[0x13] = 1; p[0x14] = 1; p[0x16] = 16; p[0x17] = 12; p[0x18] = 16; p[0x19] = 12; p[0x1A] = 9; p[0x1B] = 3; p[0x1C] = 3;
            p[0x20] = 1; p[0x21] = 1; p[0x22] = 1; p[0x23] = 2; p[0x24] = 1; p[0x25] = 1; p[0x26] = 1; p[0x27] = 1; p[0x28] = 1; p[0x29] = 1; p[0x2A] = 1; p[0x2B] = 1;
            p[0x30] = 1; p[0x31] = 1; p[0x32] = 1; p[0x33] = 1; p[0x34] = 32; p[0x40] = 1; p[0x50] = 1; p[0x60] = 1; p[0x70] = 3; p[0x71] = 2; p[0x72] = 1;
            return p;
        }

        public void Run(byte[] d, int start, int size)
        {
            int end = Math.Min(start + size, d.Length), p = start;
            while (p + 4 <= end)
            {
                uint packed = BitConverter.ToUInt32(d, p);
                p += 4;
                for (int i = 0; i < 4; i++)
                {
                    int command = (int)((packed >> (i * 8)) & 0xFF);
                    if (command == 0) continue;
                    int count = Params[command];
                    if (p + count * 4 > end) return;
                    Execute(command, d, p);
                    p += count * 4;
                }
            }
        }

        private void Execute(int command, byte[] d, int p)
        {
            uint a = BitConverter.ToUInt32(d, p);
            switch (command)
            {
                case 0x40:
                    _primitive = (int)(a & 3);
                    _strip.Clear();
                    break;
                case 0x41:
                    _primitive = -1;
                    _strip.Clear();
                    break;
                case 0x20:
                    _r = Expand(a); _g = Expand(a >> 5); _b = Expand(a >> 10);
                    break;
                case 0x22:
                    _s = (short)(a & 0xFFFF) / 16f;
                    _t = (short)(a >> 16) / 16f;
                    break;
                case 0x23:
                    _x = (short)(a & 0xFFFF); _y = (short)(a >> 16);
                    _z = (short)(BitConverter.ToUInt32(d, p + 4) & 0xFFFF);
                    Vertex();
                    break;
                case 0x24:
                    _x = Ten(a) ; _y = Ten(a >> 10); _z = Ten(a >> 20);
                    Vertex();
                    break;
                case 0x25:
                    _x = (short)(a & 0xFFFF); _y = (short)(a >> 16);
                    Vertex();
                    break;
                case 0x26:
                    _x = (short)(a & 0xFFFF); _z = (short)(a >> 16);
                    Vertex();
                    break;
                case 0x27:
                    _y = (short)(a & 0xFFFF); _z = (short)(a >> 16);
                    Vertex();
                    break;
                case 0x28:
                    _x += Diff(a); _y += Diff(a >> 10); _z += Diff(a >> 20);
                    Vertex();
                    break;
            }
        }

        private static byte Expand(uint v) { v &= 31; return (byte)((v << 3) | (v >> 2)); }
        /// <summary>VTX_10: 10 bits com 6 de fração, levados à mesma escala de 12 bits.</summary>
        private static int Ten(uint v) { int x = (int)(v & 0x3FF); if (x >= 512) x -= 1024; return x << 6; }
        /// <summary>VTX_DIFF: 10 bits com 9 de fração abaixo do último bit de 12.</summary>
        private static int Diff(uint v) { int x = (int)(v & 0x3FF); if (x >= 512) x -= 1024; return x; }

        private void Vertex()
        {
            if (_primitive < 0) return;
            _strip.Add(new ModelVertex(_x / 4096f * scale, _y / 4096f * scale, _z / 4096f * scale, _s, _t, _r, _g, _b));
            int n = _strip.Count;
            switch (_primitive)
            {
                case 0:
                    if (n == 3) { Add(0, 1, 2); _strip.Clear(); }
                    break;
                case 1:
                    if (n == 4) { Add(0, 1, 2); Add(0, 2, 3); _strip.Clear(); }
                    break;
                case 2:
                    if (n >= 3) Add(n - 3, n - 2, n - 1);
                    break;
                case 3:
                    if (n >= 4 && n % 2 == 0) { Add(n - 4, n - 3, n - 1); Add(n - 4, n - 1, n - 2); }
                    break;
            }
        }

        private void Add(int a, int b, int c) => model.Triangles.Add(new ModelTriangle(_strip[a], _strip[b], _strip[c], material));
    }
}

/// <summary>Lista 3D do formato Nitro: cabeçalho, bloco com a largura dos dados, os dados e 16 bytes de nome por item.</summary>
internal readonly struct NitroList
{
    private readonly byte[] _d;
    private readonly int _data, _size, _names;
    public int Count { get; }

    private NitroList(byte[] d, int data, int size, int names, int count) { _d = d; _data = data; _size = size; _names = names; Count = count; }

    public static NitroList Read(byte[] d, int at)
    {
        int count = d[at + 1];
        int dataHeader = at + 4 + 8 + count * 4;
        int size = BitConverter.ToUInt16(d, dataHeader);
        int data = dataHeader + 4;
        return new NitroList(d, data, size, data + count * size, count);
    }

    public int DataAt(int i) => _data + i * _size;
    public string NameAt(int i) => System.Text.Encoding.ASCII.GetString(_d, _names + i * 16, 16).TrimEnd('\0');
}

/// <summary>Texturas e paletas de um BTX0, decodificadas por nome. Cobre os sete formatos do DS.</summary>
public sealed class NitroTextureSet
{
    private readonly byte[] _d;
    private readonly int _texData, _compressedData, _compressedInfo, _palData;
    private readonly Dictionary<string, uint> _params = [];
    private readonly Dictionary<string, int> _palettes = [];
    private readonly Dictionary<(string, string?), DecodedTexture?> _cache = [];

    private NitroTextureSet(byte[] d, int tex)
    {
        _d = d;
        _texData = tex + BitConverter.ToInt32(d, tex + 0x14);
        _compressedData = tex + BitConverter.ToInt32(d, tex + 0x24);
        _compressedInfo = tex + BitConverter.ToInt32(d, tex + 0x28);
        _palData = tex + BitConverter.ToInt32(d, tex + 0x38);
        var textures = NitroList.Read(d, tex + BitConverter.ToUInt16(d, tex + 0x0E));
        for (int i = 0; i < textures.Count; i++) _params[textures.NameAt(i)] = BitConverter.ToUInt32(d, textures.DataAt(i));
        var palettes = NitroList.Read(d, tex + BitConverter.ToInt32(d, tex + 0x34));
        for (int i = 0; i < palettes.Count; i++) _palettes[palettes.NameAt(i)] = BitConverter.ToUInt16(d, palettes.DataAt(i));
    }

    public static NitroTextureSet? TryRead(byte[] file)
    {
        try
        {
            if (file.Length < 0x14 || file[0] != 'B' || file[1] != 'T' || file[2] != 'X' || file[3] != '0') return null;
            return new NitroTextureSet(file, BitConverter.ToInt32(file, 0x10));
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException) { return null; }
    }

    public DecodedTexture? Get(string? texture, string? palette)
    {
        if (texture is null) return null;
        if (_cache.TryGetValue((texture, palette), out var known)) return known;
        DecodedTexture? image = null;
        if (_params.TryGetValue(texture, out var param))
            try { image = Decode(param, palette); }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException) { }
        return _cache[(texture, palette)] = image;
    }

    private DecodedTexture? Decode(uint param, string? palette)
    {
        int width = 8 << (int)((param >> 20) & 7), height = 8 << (int)((param >> 23) & 7), format = (int)((param >> 26) & 7);
        bool transparent = ((param >> 29) & 1) != 0;
        int source = (int)(param & 0xFFFF) * 8;
        int pal = palette is not null && _palettes.TryGetValue(palette, out var p) ? p : 0;
        int palShift = format == 2 ? 3 : 3;
        int palBase = _palData + pal * (1 << palShift);
        var pixels = new uint[width * height];
        uint Color(int index, int alpha = 255)
        {
            int c = BitConverter.ToUInt16(_d, palBase + index * 2);
            return Rgba(c, alpha);
        }

        switch (format)
        {
            case 1: // A3I5
                for (int i = 0; i < pixels.Length; i++)
                {
                    int v = _d[_texData + source + i];
                    pixels[i] = Color(v & 31, (v >> 5) * 255 / 7);
                }
                break;
            case 2:
                for (int i = 0; i < pixels.Length; i++)
                {
                    int v = (_d[_texData + source + i / 4] >> (i % 4 * 2)) & 3;
                    pixels[i] = v == 0 && transparent ? 0 : Color(v);
                }
                break;
            case 3:
                for (int i = 0; i < pixels.Length; i++)
                {
                    int v = (_d[_texData + source + i / 2] >> (i % 2 * 4)) & 15;
                    pixels[i] = v == 0 && transparent ? 0 : Color(v);
                }
                break;
            case 4:
                for (int i = 0; i < pixels.Length; i++)
                {
                    int v = _d[_texData + source + i];
                    pixels[i] = v == 0 && transparent ? 0 : Color(v);
                }
                break;
            case 5:
                DecodeCompressed(pixels, width, height, source, palBase);
                break;
            case 6: // A5I3
                for (int i = 0; i < pixels.Length; i++)
                {
                    int v = _d[_texData + source + i];
                    pixels[i] = Color(v & 7, (v >> 3) * 255 / 31);
                }
                break;
            case 7:
                for (int i = 0; i < pixels.Length; i++)
                {
                    int c = BitConverter.ToUInt16(_d, _texData + source + i * 2);
                    pixels[i] = (c & 0x8000) != 0 ? Rgba(c, 255) : 0;
                }
                break;
            default:
                return null;
        }
        return new DecodedTexture(width, height, pixels);
    }

    /// <summary>Formato 4x4: cada bloco tem 4 cores de 2 bits e um índice que diz como obter a paleta do bloco.</summary>
    private void DecodeCompressed(uint[] pixels, int width, int height, int source, int palBase)
    {
        int blocksX = width / 4, blocksY = height / 4;
        int infoBase = _compressedInfo + source / 2;
        for (int by = 0; by < blocksY; by++)
            for (int bx = 0; bx < blocksX; bx++)
            {
                int block = by * blocksX + bx;
                uint bits = BitConverter.ToUInt32(_d, _texData + source + block * 4);
                int info = BitConverter.ToUInt16(_d, infoBase + block * 2);
                int pal = palBase + (info & 0x3FFF) * 4, mode = info >> 14;
                uint C(int n) => Rgba(BitConverter.ToUInt16(_d, pal + n * 2), 255);
                var colors = new uint[4];
                colors[0] = C(0);
                colors[1] = C(1);
                switch (mode)
                {
                    case 0: colors[2] = C(2); colors[3] = 0; break;
                    case 1: colors[2] = Mix(colors[0], colors[1], 1, 1, 2); colors[3] = 0; break;
                    case 2: colors[2] = C(2); colors[3] = C(3); break;
                    default: colors[2] = Mix(colors[0], colors[1], 5, 3, 8); colors[3] = Mix(colors[0], colors[1], 3, 5, 8); break;
                }
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        pixels[(by * 4 + y) * width + bx * 4 + x] = colors[(bits >> ((y * 4 + x) * 2)) & 3];
            }
    }

    private static uint Rgba(int c, int alpha)
    {
        static uint E(int v) { v &= 31; return (uint)((v << 3) | (v >> 2)); }
        return E(c) | E(c >> 5) << 8 | E(c >> 10) << 16 | (uint)alpha << 24;
    }

    private static uint Mix(uint a, uint b, uint wa, uint wb, uint div)
    {
        uint Part(int shift) => (((a >> shift) & 255) * wa + ((b >> shift) & 255) * wb) / div & 255;
        return Part(0) | Part(8) << 8 | Part(16) << 16 | 255u << 24;
    }
}

public sealed record DecodedTexture(int Width, int Height, uint[] Pixels);

/// <summary>Desenha triângulos de cima para baixo: x e z viram a imagem, e fica o pixel mais alto.</summary>
public sealed class TopDownRaster
{
    private readonly float[] _depth;
    public RgbaImage Image { get; }
    /// <summary>Pixels por unidade de mundo.</summary>
    public float Scale { get; }

    public TopDownRaster(int width, int height, float scale, (byte R, byte G, byte B) background)
    {
        Image = new RgbaImage(width, height);
        Scale = scale;
        _depth = new float[width * height];
        Array.Fill(_depth, float.NegativeInfinity);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                Image.Set(x, y, background.R, background.G, background.B);
    }

    public void Draw(NsbmdModel model, float offsetX, float offsetY, float offsetZ, Func<string?, string?, DecodedTexture?> textures)
    {
        foreach (var t in model.Triangles)
        {
            var material = t.Material >= 0 && t.Material < model.Materials.Count ? model.Materials[t.Material] : null;
            var texture = material is null ? null : textures(material.Texture, material.Palette);
            // textura que não está no conjunto desta área: sem ela sairia um borrão branco
            if (material?.Texture is not null && texture is null) continue;
            if (material?.Alpha == 0) continue;
            // os riscos de vento e similares ficam muito acima do mapa, como efeito
            if (material?.Texture == "wind") continue;
            Fill(t, material, texture, offsetX, offsetY, offsetZ);
        }
    }

    private void Fill(ModelTriangle t, ModelMaterial? m, DecodedTexture? tex, float ox, float oy, float oz)
    {
        float ax = (t.A.X + ox) * Scale, az = (t.A.Z + oz) * Scale;
        float bx = (t.B.X + ox) * Scale, bz = (t.B.Z + oz) * Scale;
        float cx = (t.C.X + ox) * Scale, cz = (t.C.Z + oz) * Scale;
        float area = (bx - ax) * (cz - az) - (bz - az) * (cx - ax);
        if (Math.Abs(area) < 0.0001f) return; // parede vista de cima

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))));
        int maxX = Math.Min(Image.Width - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(az, MathF.Min(bz, cz))));
        int maxY = Math.Min(Image.Height - 1, (int)MathF.Ceiling(MathF.Max(az, MathF.Max(bz, cz))));

        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((bx - px) * (cz - py) - (bz - py) * (cx - px)) / area;
                float w1 = ((cx - px) * (az - py) - (cz - py) * (ax - px)) / area;
                float w2 = 1 - w0 - w1;
                if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;
                float height = t.A.Y * w0 + t.B.Y * w1 + t.C.Y * w2 + oy;
                int at = y * Image.Width + x;
                if (height < _depth[at]) continue;

                float r = (t.A.R * w0 + t.B.R * w1 + t.C.R * w2) / 255f;
                float g = (t.A.G * w0 + t.B.G * w1 + t.C.G * w2) / 255f;
                float b = (t.A.B * w0 + t.B.B * w1 + t.C.B * w2) / 255f;
                uint color = 0xFFFFFFFF;
                if (tex is not null)
                {
                    float s = t.A.S * w0 + t.B.S * w1 + t.C.S * w2;
                    float v = t.A.T * w0 + t.B.T * w1 + t.C.T * w2;
                    color = tex.Pixels[Wrap(v, tex.Height, m?.RepeatT == true, m?.FlipT == true) * tex.Width + Wrap(s, tex.Width, m?.RepeatS == true, m?.FlipS == true)];
                    if (color >> 24 < 128) continue; // furo da textura: o que está embaixo aparece
                }
                byte red = (byte)((color & 255) * r), green = (byte)(((color >> 8) & 255) * g), blue = (byte)(((color >> 16) & 255) * b);
                int alpha = m?.Alpha ?? 31;
                if (alpha < 31)
                {
                    // translúcido (sombra): mistura com o que já está ali e deixa o que vier depois passar por cima
                    int i = at * 4;
                    var px4 = Image.Pixels;
                    Image.Set(x, y, (byte)((red * alpha + px4[i] * (31 - alpha)) / 31), (byte)((green * alpha + px4[i + 1] * (31 - alpha)) / 31), (byte)((blue * alpha + px4[i + 2] * (31 - alpha)) / 31));
                    continue;
                }
                _depth[at] = height;
                Image.Set(x, y, red, green, blue);
            }
    }

    private static int Wrap(float coordinate, int size, bool repeat, bool flip)
    {
        int c = (int)MathF.Floor(coordinate);
        if (!repeat) return Math.Clamp(c, 0, size - 1);
        int m = ((c % (size * 2)) + size * 2) % (size * 2);
        if (m < size) return m;
        return flip ? size * 2 - 1 - m : m - size;
    }
}
