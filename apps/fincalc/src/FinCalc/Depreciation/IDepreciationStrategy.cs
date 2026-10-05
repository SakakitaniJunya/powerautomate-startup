namespace FinCalc.Depreciation;

/// <summary>
/// 償却方法1種類ぶんのスケジュール生成を担う戦略。
/// 新しい償却方法（部署独自ルール等）を足すときは、このインターフェースを実装して
/// <see cref="DepreciationStrategies"/> に登録する。既存コードの変更は不要。
/// </summary>
public interface IDepreciationStrategy
{
    /// <summary>この戦略が担当する償却方法。</summary>
    DepreciationMethod Method { get; }

    /// <summary>期末帳簿価額が備忘価額1円になるまでのスケジュールを返す。</summary>
    IReadOnlyList<DepreciationEntry> Schedule(
        long cost, int life, int firstYearMonths, RoundMode round);
}

/// <summary>定額法: 取得価額 × 定額法償却率。初年度は月割り。</summary>
public sealed class StraightLineStrategy : IDepreciationStrategy
{
    public DepreciationMethod Method => DepreciationMethod.StraightLine;

    public IReadOnlyList<DepreciationEntry> Schedule(
        long cost, int life, int firstYearMonths, RoundMode round)
    {
        var rate = RateTable.StraightLineRate(life);
        var rows = new List<DepreciationEntry>(life + 1);
        long book = cost, acc = 0;

        // 初年度月割のとき償却期間は life+1 年度に延びる
        var maxYears = firstYearMonths == 12 ? life : life + 1;
        for (var year = 1; year <= maxYears && book > 1; year++)
        {
            var raw = cost * rate * (year == 1 ? firstYearMonths : 12) / 12m;
            var expense = Math.Min(MoneyRound.Apply(raw, round), book - 1);
            book -= expense;
            acc += expense;
            rows.Add(new(year, "straightLine", rate, expense, acc, book, false));
        }
        return rows;
    }
}

/// <summary>200%定率法: 償却保証額を下回った年度から改定償却率による均等償却へ切替。</summary>
public sealed class DecliningBalance200Strategy : IDepreciationStrategy
{
    public DepreciationMethod Method => DepreciationMethod.DecliningBalance200;

    public IReadOnlyList<DepreciationEntry> Schedule(
        long cost, int life, int firstYearMonths, RoundMode round)
    {
        var r = RateTable.DecliningBalance200(life);
        decimal? guarantee = r.GuaranteeRate is { } g ? cost * g : null;
        decimal? revisedCost = null;
        var rows = new List<DepreciationEntry>(life + 1);
        long book = cost, acc = 0;

        var maxYears = firstYearMonths == 12 ? life : life + 1;
        for (var year = 1; year <= maxYears && book > 1; year++)
        {
            // 改定判定は按分前の率で行う（耐用年数省令5条4項）
            if (revisedCost is null && guarantee is { } gv && book * r.Rate < gv)
                revisedCost = book;

            decimal rate;
            decimal raw;
            if (revisedCost is { } rc)
            {
                rate = r.RevisedRate!.Value;
                raw = rc * rate;
            }
            else
            {
                rate = r.Rate;
                raw = book * rate * (year == 1 ? firstYearMonths : 12) / 12m;
            }

            var expense = Math.Min(MoneyRound.Apply(raw, round), book - 1);
            book -= expense;
            acc += expense;
            rows.Add(new(year, "decliningBalance200", rate, expense, acc, book, revisedCost is not null));
        }
        return rows;
    }
}

/// <summary>
/// 償却方法 → 戦略 のレジストリ。<see cref="With"/> で新しい戦略を合成した
/// 別インスタンスを作れる（既定セットは変更しない）。
/// </summary>
public sealed class DepreciationStrategies
{
    private readonly Dictionary<DepreciationMethod, IDepreciationStrategy> _map;

    public DepreciationStrategies(IEnumerable<IDepreciationStrategy> strategies) =>
        _map = strategies.ToDictionary(s => s.Method);

    public static DepreciationStrategies CreateDefault() => new(
        new IDepreciationStrategy[] { new StraightLineStrategy(), new DecliningBalance200Strategy() });

    public static DepreciationStrategies Default { get; } = CreateDefault();

    public IDepreciationStrategy Resolve(DepreciationMethod method) =>
        _map.TryGetValue(method, out var s)
            ? s
            : throw new ArgumentException($"償却方法 {method} の戦略が登録されていません");

    /// <summary>戦略を上書き/追加した新しいセットを返す。</summary>
    public DepreciationStrategies With(IDepreciationStrategy strategy) =>
        new(_map.Values.Where(s => s.Method != strategy.Method).Append(strategy));
}
