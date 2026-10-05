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

public static class DepreciationCalculator
{
    /// <summary>
    /// 償却スケジュール生成。期末帳簿価額は備忘価額1円まで。
    /// firstYearMonths: 初年度の事業供用月数（1〜12）。
    /// </summary>
    public static IReadOnlyList<DepreciationEntry> Schedule(
        long acquisitionCost,
        int usefulLifeYears,
        DepreciationMethod method,
        int firstYearMonths = 12,
        RoundMode round = RoundMode.Floor)
    {
        if (acquisitionCost <= 0) throw new ArgumentOutOfRangeException(nameof(acquisitionCost));
        if (firstYearMonths is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(firstYearMonths));

        return method == DepreciationMethod.StraightLine
            ? StraightLine(acquisitionCost, usefulLifeYears, firstYearMonths, round)
            : DecliningBalance(acquisitionCost, usefulLifeYears, firstYearMonths, round);
    }

    private static List<DepreciationEntry> StraightLine(
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

    private static List<DepreciationEntry> DecliningBalance(
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
