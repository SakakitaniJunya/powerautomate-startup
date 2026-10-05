namespace FinCalc.Tax;

/// <summary>
/// 報酬・料金等の源泉所得税（復興特別所得税込み）。
/// 支払金額100万円以下: 10.21%、超過分: 20.42%（超過部分 + 102,100円）。
/// 税額は1円未満切り捨て。
/// </summary>
public sealed class WithholdingTaxCalculator
{
    public sealed record Result(long GrossAmount, long Tax, long NetPayment, decimal EffectiveRate);

    public Result ForFee(long grossAmount)
    {
        if (grossAmount < 0) throw new ArgumentOutOfRangeException(nameof(grossAmount));

        long tax = grossAmount <= 1_000_000
            ? MoneyRound.Apply(grossAmount * 0.1021m, RoundMode.Floor)
            : MoneyRound.Apply((grossAmount - 1_000_000m) * 0.2042m + 102_100m, RoundMode.Floor);

        return new(
            grossAmount,
            tax,
            grossAmount - tax,
            grossAmount == 0 ? 0m : decimal.Round(tax / (decimal)grossAmount, 6));
    }
}
