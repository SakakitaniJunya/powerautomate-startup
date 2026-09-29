namespace FinCalc;

/// <summary>端数処理モード。日本の税務実務に合わせた円単位丸め。</summary>
public enum RoundMode
{
    /// <summary>切り捨て（消費税・償却費の一般的な扱い）</summary>
    Floor,
    /// <summary>切り上げ</summary>
    Ceiling,
    /// <summary>四捨五入（0.5 は +∞ 側）</summary>
    Nearest,
}

public static class MoneyRound
{
    public static long Apply(decimal value, RoundMode mode) => mode switch
    {
        RoundMode.Floor => (long)Math.Floor(value),
        RoundMode.Ceiling => (long)Math.Ceiling(value),
        _ => (long)Math.Round(value, 0, MidpointRounding.AwayFromZero),
    };
}
