using FinCalc;
using FinCalc.Depreciation;
using Xunit;

namespace FinCalc.Tests;

public class DepreciationTests
{
    // 国税庁 No.2106 の例: 100万円・耐用年数10年・定額法 → 毎年100,000円、最終年は備忘価額1円残し
    [Fact]
    public void StraightLine_NtaExample_10years()
    {
        var rows = DepreciationCalculator.Schedule(1_000_000, 10, DepreciationMethod.StraightLine);

        Assert.Equal(10, rows.Count);
        Assert.All(rows.Take(9), r => Assert.Equal(100_000, r.Expense));
        Assert.Equal(99_999, rows[9].Expense);
        Assert.Equal(1, rows[9].BookValueEnd);
        Assert.Equal(999_999, rows[9].Accumulated);
    }

    // 同例・定率法200%: 7年目に改定償却へ切替（調整前償却額52,429 < 償却保証額65,520）
    [Fact]
    public void DecliningBalance_NtaExample_10years()
    {
        var rows = DepreciationCalculator.Schedule(1_000_000, 10, DepreciationMethod.DecliningBalance200);

        Assert.Equal(200_000, rows[0].Expense);
        Assert.Equal(160_000, rows[1].Expense);
        Assert.Equal(128_000, rows[2].Expense);
        Assert.Equal(102_400, rows[3].Expense);
        Assert.Equal(81_920, rows[4].Expense);
        Assert.Equal(65_536, rows[5].Expense);
        Assert.False(rows[5].IsRevised);

        // 7年目: 期首262,144 × 0.2 = 52,428.8 < 65,520 → 改定取得価額262,144 × 0.25 = 65,536
        Assert.True(rows[6].IsRevised);
        Assert.Equal(65_536, rows[6].Expense);
        Assert.Equal(65_536, rows[7].Expense);
        Assert.Equal(65_536, rows[8].Expense);
        Assert.Equal(65_535, rows[9].Expense); // 残り65,536 - 備忘1円
        Assert.Equal(1, rows[9].BookValueEnd);
        Assert.Equal(999_999, rows[9].Accumulated);
    }

    // 国税庁 No.5410 の例: 100万円・耐用年数8年・200%定率法・切捨て → 償却保証額79,090
    [Fact]
    public void DecliningBalance_NtaExample_8years()
    {
        var rows = DepreciationCalculator.Schedule(1_000_000, 8, DepreciationMethod.DecliningBalance200);

        Assert.Equal(250_000, rows[0].Expense);
        Assert.Equal(187_500, rows[1].Expense);
        Assert.Equal(140_625, rows[2].Expense);
        Assert.Equal(105_468, rows[3].Expense); // 421,875 × 0.25 = 105,468.75 → 切捨
        Assert.Equal(79_101, rows[4].Expense);  // 316,407 × 0.25 = 79,101.75 → 切捨

        // 6年目: 237,306 × 0.25 = 59,326.5 < 79,090 → 改定 237,306 × 0.334 = 79,260.204 → 79,260
        Assert.True(rows[5].IsRevised);
        Assert.Equal(79_260, rows[5].Expense);
        Assert.Equal(79_260, rows[6].Expense);
        Assert.Equal(78_785, rows[7].Expense); // 78,786 - 1円
        Assert.Equal(1, rows[7].BookValueEnd);
        Assert.Equal(999_999, rows[7].Accumulated);
    }

    [Fact]
    public void StraightLine_FirstYearProration()
    {
        var rows = DepreciationCalculator.Schedule(
            1_200_000, 10, DepreciationMethod.StraightLine, firstYearMonths: 6);

        Assert.Equal(60_000, rows[0].Expense); // 120,000 × 6/12
        Assert.Equal(120_000, rows[1].Expense);
        // 月割分だけ償却期間が延び、11年目に備忘価額1円まで償却する
        Assert.Equal(11, rows.Count);
        Assert.Equal(1, rows[^1].BookValueEnd);
        Assert.Equal(1_199_999, rows.Sum(r => r.Expense));
    }

    [Fact]
    public void DecliningBalance_TwoYears_FullRate()
    {
        var rows = DepreciationCalculator.Schedule(300_000, 2, DepreciationMethod.DecliningBalance200);
        // 率1.000で1年目にほぼ全額償却（備忘価額1円のみ残す）。簿価1円到達で打ち切り
        Assert.Single(rows);
        Assert.Equal(299_999, rows[0].Expense);
        Assert.Equal(1, rows[0].BookValueEnd);
    }

    [Fact]
    public void DecliningBalance_UnsupportedLife_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DepreciationCalculator.Schedule(1_000_000, 99, DepreciationMethod.DecliningBalance200));
    }
}
