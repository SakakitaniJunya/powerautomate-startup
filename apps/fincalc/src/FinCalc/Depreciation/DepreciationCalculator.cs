namespace FinCalc.Depreciation;

public enum DepreciationMethod
{
    /// <summary>定額法</summary>
    StraightLine,
    /// <summary>定率法（200%・平成24年4月1日以後取得分）</summary>
    DecliningBalance200,
}

/// <summary>1事業年度分の償却結果</summary>
public sealed record DepreciationEntry(
    int Year,
    string Method,
    decimal Rate,
    long Expense,
    long Accumulated,
    long BookValueEnd,
    bool IsRevised);

/// <summary>
/// 償却スケジュール生成の窓口。計算本体は <see cref="IDepreciationStrategy"/> の
/// 実装に委譲し、ここでは入力検証と戦略の解決だけを行う。
/// 部署独自の償却方法を足すときはこのクラスを触らず
/// <see cref="DepreciationStrategies"/> に戦略を登録する。
/// </summary>
public sealed class DepreciationCalculator
{
    private readonly DepreciationStrategies _strategies;

    /// <summary>既定の戦略セットを使う共有インスタンス。</summary>
    public static DepreciationCalculator Default { get; } = new();

    public DepreciationCalculator() : this(DepreciationStrategies.Default) { }

    public DepreciationCalculator(DepreciationStrategies strategies) =>
        _strategies = strategies;

    /// <summary>
    /// 償却スケジュール生成。期末帳簿価額は備忘価額1円まで。
    /// firstYearMonths: 初年度の事業供用月数（1〜12）。
    /// </summary>
    public IReadOnlyList<DepreciationEntry> Schedule(
        long acquisitionCost,
        int usefulLifeYears,
        DepreciationMethod method,
        int firstYearMonths = 12,
        RoundMode round = RoundMode.Floor)
    {
        if (acquisitionCost <= 0) throw new ArgumentOutOfRangeException(nameof(acquisitionCost));
        if (firstYearMonths is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(firstYearMonths));

        return _strategies.Resolve(method)
            .Schedule(acquisitionCost, usefulLifeYears, firstYearMonths, round);
    }
}
