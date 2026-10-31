using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;

namespace PokeHub.Core.CodeInjection;

/// <summary>
/// Expansão do ARM9, a mesma do DSPRE (ROM Toolbox, valores de ToolboxDB.cs): uma chamada no início do Main() carrega um
/// arquivo de NARC ("overlay sintético") em 0x023C8000, memória que o heap do jogo não usa. O código do hub mora nesse arquivo.
/// Usar o mesmo patch mantém a ROM compatível com os outros patches do DSPRE, que também gravam nesse arquivo.
/// GEN5: BW/B2W2 têm outro mapa de memória.
/// </summary>
public sealed record Arm9Expansion(uint BranchAddress, byte[] BranchCode, uint InitAddress, byte[] InitCode, string SyntheticNarc, int SyntheticFile)
{
    public const uint LoadAddress = 0x023C8000;
    /// <summary>Tamanho que o DSPRE reserva para o arquivo: 88 KB.</summary>
    public const int FileSize = 0x16000;
    /// <summary>
    /// Trecho do arquivo usado pelo hub. Abaixo dele fica a tabela de comandos de script do DSPRE (0x200 por padrão);
    /// acima, a rotina de câmeras dinâmicas (0x115B0). Só se grava em área zerada, então um patch do DSPRE em outro lugar é detectado.
    /// </summary>
    public const int WindowStart = 0x2000, WindowEnd = 0x11000;

    /// <summary>As duas instruções do Main() que a chamada substitui; a rotina de carga as executa antes de voltar.</summary>
    private static readonly byte[] OriginalBranchSite = [0x00, 0x20, 0x03, 0x21];

    public static Arm9Expansion? For(Gen4Game game)
    {
        if (!Init.TryGetValue((Family(game), game.Language), out var init)) return null;
        uint branchAddress = game.IsHgss ? (game.Japanese ? 0x02000CB4u : 0x02000CD0u) : game.IsPlatinum ? 0x02000CB4u : 0x02000C80u;
        return new Arm9Expansion(branchAddress, ArmEncoding.Call(branchAddress, init.Address | 1, siteThumb: true), init.Address, Convert.FromHexString(init.Code),
            game.IsHgss ? @"a\0\2\8" : @"data\weather_sys.narc", game.IsHgss ? 0 : 9);
    }

    public bool IsApplied(ReadOnlySpan<byte> arm9, uint arm9Base) =>
        arm9.Slice((int)(BranchAddress - arm9Base), BranchCode.Length).SequenceEqual(BranchCode)
        && arm9.Slice((int)(InitAddress - arm9Base), InitCode.Length).SequenceEqual(InitCode);

    /// <summary>Grava o patch se faltar. Recusa quando o ponto de entrada ou a área livre não estão como no jogo limpo (ex.: hg-engine).</summary>
    public bool EnsureApplied(byte[] arm9, uint arm9Base)
    {
        if (IsApplied(arm9, arm9Base)) return false;
        int branch = (int)(BranchAddress - arm9Base), init = (int)(InitAddress - arm9Base);
        if (!arm9.AsSpan(branch, BranchCode.Length).SequenceEqual(OriginalBranchSite) || !IsDisposable(arm9.AsSpan(init, InitCode.Length)))
            throw new InvalidDataException(Loc.T("core.inject.expansionConflict", $"0x{BranchAddress:X8}", $"0x{InitAddress:X8}"));
        BranchCode.CopyTo(arm9, branch);
        InitCode.CopyTo(arm9, init);
        return true;
    }

    /// <summary>
    /// Preenchimento (HGSS) ou o texto da mensagem de assert do SDK (Platinum), que o patch do DSPRE sobrescreve: "Assertion (%s) failed in".
    /// Qualquer outra coisa pode ser código ou um patch alheio. A mensagem só apareceria num travamento, e sairia cortada.
    /// </summary>
    private static bool IsDisposable(ReadOnlySpan<byte> area) =>
        (area.IndexOfAnyExcept(area[0]) < 0 && area[0] is 0x00 or 0xFF) || area.StartsWith("Assertion ("u8);

    private enum GameFamily { Dp, Platinum, Hgss }
    private static GameFamily Family(Gen4Game g) => g.IsHgss ? GameFamily.Hgss : g.IsPlatinum ? GameFamily.Platinum : GameFamily.Dp;

    /// <summary>
    /// Endereço e código da rotina de carga. A chamada é calculada em vez de copiada: a tabela do DSPRE (2.3.4) tem o salto de DP alemão
    /// apontando para o endereço de Pt alemão e o endereço de HGSS alemão trocado (0x02110B00 no lugar de 0x021110B0, o único
    /// em que o BL interno da rotina cai na mesma função 0x02007508 das versões italiana e francesa).
    /// </summary>
    private static readonly Dictionary<(GameFamily, Gen4Language), (uint Address, string Code)> Init = new()
    {
        [(GameFamily.Dp, Gen4Language.English)] = (0x02106B3C, "FCB5044841210922FFF6D4FC00200321FCBD000000803C0200"),
        [(GameFamily.Dp, Gen4Language.Spanish)] = (0x02106CDC, "FCB5044841210922FFF604FC00200321FCBD000000803C0200"),
        [(GameFamily.Dp, Gen4Language.Italian)] = (0x02106C1C, "FCB5044841210922FFF664FC00200321FCBD000000803C0200"),
        [(GameFamily.Dp, Gen4Language.French)] = (0x02106CBC, "FCB5044841210922FFF614FC00200321FCBD000000803C0200"),
        [(GameFamily.Dp, Gen4Language.German)] = (0x02106C7C, "FCB5044841210922FFF634FC00200321FCBD000000803C0200"),
        [(GameFamily.Platinum, Gen4Language.English)] = (0x02101574, "FCB504484121092205F792FA00200321FCBD000000803C0200"),
        [(GameFamily.Platinum, Gen4Language.Spanish)] = (0x02101770, "FCB504484121092205F79EF900200321FCBD000000803C0200"),
        [(GameFamily.Platinum, Gen4Language.Italian)] = (0x021016D0, "FCB504484121092205F7EEF900200321FCBD000000803C0200"),
        [(GameFamily.Platinum, Gen4Language.French)] = (0x02101750, "FCB504484121092205F7AEF900200321FCBD000000803C0200"),
        [(GameFamily.Platinum, Gen4Language.German)] = (0x02101710, "FCB504484121092205F7CEF900200321FCBD000000803C0200"),
        [(GameFamily.Platinum, Gen4Language.Japanese)] = (0x02100968, "FCB504484121092206F738F800200321FCBD000000803C0200"),
        [(GameFamily.Hgss, Gen4Language.English)] = (0x02110334, "FCB50548C0461C210022024DA84700200321FCBD0975000200803C02"),
        [(GameFamily.Hgss, Gen4Language.Spanish)] = (0x02110354, "FCB50548C0461C210022024DA84700200321FCBD0975000200803C02"),
        [(GameFamily.Hgss, Gen4Language.Italian)] = (0x02111070, "FCB504481C210022F6F646FA00200321FCBD000000803C0200"),
        [(GameFamily.Hgss, Gen4Language.French)] = (0x021110F0, "FCB504481C210022F6F606FA00200321FCBD000000803C0200"),
        [(GameFamily.Hgss, Gen4Language.German)] = (0x021110B0, "FCB504481C210022F6F626FA00200321FCBD000000803C0200"),
        [(GameFamily.Hgss, Gen4Language.Japanese)] = (0x02110604, "FCB504481C210022F6F608FF00200321FCBD000000803C0200"),
    };
}
