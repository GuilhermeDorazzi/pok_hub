namespace PokeHub.Core.Rom;

public enum BaseGame
{
    Unknown,
    Diamond, Pearl, Platinum, HeartGold, SoulSilver,
    // GEN5: adicionar Black, White, Black2, White2
}

public static class GameInfo
{
    /// <summary>Mapeia os 3 primeiros caracteres do game code do header para o jogo.</summary>
    public static BaseGame FromGameCode(string gameCode)
    {
        if (gameCode.Length < 3) return BaseGame.Unknown;
        return gameCode[..3].ToUpperInvariant() switch
        {
            "ADA" => BaseGame.Diamond,
            "APA" => BaseGame.Pearl,
            "CPU" => BaseGame.Platinum,
            "IPK" => BaseGame.HeartGold,
            "IPG" => BaseGame.SoulSilver,
            // GEN5: "IRB" => Black, "IRA" => White, "IRE" => Black2, "IRD" => White2
            _ => BaseGame.Unknown,
        };
    }

    public static int Generation(BaseGame game) => game switch
    {
        BaseGame.Diamond or BaseGame.Pearl or BaseGame.Platinum or BaseGame.HeartGold or BaseGame.SoulSilver => 4,
        // GEN5: Black/White/Black2/White2 => 5
        _ => 0,
    };

    /// <summary>Região pelo 4º caractere do game code.</summary>
    public static string Region(string gameCode) => gameCode.Length < 4 ? "?" : gameCode[3] switch
    {
        'E' => "USA", 'P' => "EUR", 'J' => "JPN", 'K' => "KOR", 'D' => "GER", 'F' => "FRA", 'I' => "ITA", 'S' => "SPA",
        _ => gameCode[3].ToString(),
    };
}
