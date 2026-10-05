namespace FinCalc.Tax;

/// <summary>消費税計算。標準税率10% / 軽減税率8%。インボイス方式は税率ごとに1回の端数処理。</summary>
public sealed class ConsumptionTaxCalculator
{
    public const decimal StandardRate = 0.10m;
    public const decimal ReducedRate = 0.08m;

    public sealed record TaxLine(long Net, decimal Rate, long Tax, long Gross);
    public sealed record RateGroup(decimal Rate, long NetTotal, long TaxTotal);
    public sealed record InvoiceResult(IReadOnlyList<RateGroup> Groups, long NetTotal, long TaxTotal, long GrossTotal);

    /// <summary>税抜 → 税込（1明細）</summary>
    public TaxLine AddTax(long net, decimal rate, RoundMode round = RoundMode.Floor)
    {
        ValidateRate(rate);
        var tax = MoneyRound.Apply(net * rate, round);
        return new(net, rate, tax, net + tax);
    }

    /// <summary>税込 → 税抜（1明細）</summary>
    public TaxLine ExtractTax(long gross, decimal rate, RoundMode round = RoundMode.Floor)
    {
        ValidateRate(rate);
        var net = MoneyRound.Apply(gross / (1m + rate), round);
        return new(net, rate, gross - net, gross);
    }

    /// <summary>
    /// 適格請求書（インボイス）の税額計算: 税率ごとに課税標準を合計してから1回だけ端数処理。
    /// </summary>
    public InvoiceResult Invoice(IEnumerable<(long net, decimal rate)> lines, RoundMode round = RoundMode.Floor)
    {
        var groups = lines
            .GroupBy(l => l.rate)
            .Select(g =>
            {
                ValidateRate(g.Key);
                var netTotal = g.Sum(x => x.net);
                return new RateGroup(g.Key, netTotal, MoneyRound.Apply(netTotal * g.Key, round));
            })
            .OrderByDescending(g => g.Rate)
            .ToList();

        return new(
            groups,
            groups.Sum(g => g.NetTotal),
            groups.Sum(g => g.TaxTotal),
            groups.Sum(g => g.NetTotal + g.TaxTotal));
    }

    private static void ValidateRate(decimal rate)
    {
        if (rate != StandardRate && rate != ReducedRate)
            throw new ArgumentOutOfRangeException(nameof(rate), $"税率は 0.10 または 0.08。指定値: {rate}");
    }
}
