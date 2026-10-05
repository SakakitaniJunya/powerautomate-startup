using FinCalc.Depreciation;

namespace FinCalc.Cli.Commands;

/// <summary>dep schedule — 償却スケジュール生成。</summary>
public sealed class DepScheduleCommand : ICommand
{
    private readonly DepreciationCalculator _calculator;

    public DepScheduleCommand() : this(DepreciationCalculator.Default) { }
    public DepScheduleCommand(DepreciationCalculator calculator) => _calculator = calculator;

    public string Name => "dep schedule";
    public string Usage => "dep schedule          --method straight|declining --cost N --life Y [--months M] [--round floor|ceiling|nearest]";

    public object Run(CommandArgs a)
    {
        var method = a.Req("method") switch
        {
            "straight" => DepreciationMethod.StraightLine,
            "declining" or "db200" => DepreciationMethod.DecliningBalance200,
            var m => throw new ArgumentException($"method は straight|declining。指定値: {m}")
        };
        var cost = a.ReqLong("cost");
        var life = a.ReqInt("life");
        var months = a.OptInt("months") ?? 12;
        var round = a.OptRound();

        var rows = _calculator.Schedule(cost, life, method, months, round);
        return new
        {
            cost,
            usefulLifeYears = life,
            method = method.ToString(),
            firstYearMonths = months,
            rounding = round.ToString().ToLowerInvariant(),
            rate = method == DepreciationMethod.StraightLine
                ? RateTable.StraightLineRate(life)
                : RateTable.DecliningBalance200(life).Rate,
            schedule = rows
        };
    }
}

/// <summary>rates — 耐用年数から償却率・改定償却率・保証率を参照。</summary>
public sealed class RatesCommand : ICommand
{
    public string Name => "rates";
    public string Usage => "rates                 --life Y";

    public object Run(CommandArgs a)
    {
        var life = a.ReqInt("life");
        var db = RateTable.DecliningBalance200(life);
        return new
        {
            usefulLifeYears = life,
            straightLineRate = RateTable.StraightLineRate(life),
            decliningBalanceRate = db.Rate,
            revisedRate = db.RevisedRate,
            guaranteeRate = db.GuaranteeRate,
        };
    }
}
