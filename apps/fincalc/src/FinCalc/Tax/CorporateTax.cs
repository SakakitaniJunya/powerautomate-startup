namespace FinCalc.Tax;

/// <summary>
/// 法人税の概算（中小法人: 資本金1億円以下・所得800万円以下部分に軽減税率15%）。
/// 地方法人税 = 法人税額 × 10.3%。住民税・事業税は含まない簡易モデル。
/// </summary>
public static class CorporateTax
{
    public sealed record Result(
        long TaxableIncome,
        long NationalTax,
        long LocalCorporateTax,
        long Total);

    public static Result Estimate(long taxableIncome)
    {
        if (taxableIncome <= 0)
            return new(taxableIncome, 0, 0, 0);

        var reduced = Math.Min(taxableIncome, 8_000_000L) * 0.15m;
        var standard = Math.Max(0L, taxableIncome - 8_000_000L) * 0.232m;
        var national = MoneyRound.Apply(reduced + standard, RoundMode.Floor);
        var local = MoneyRound.Apply(national * 0.103m, RoundMode.Floor);

        return new(taxableIncome, national, local, national + local);
    }
}
