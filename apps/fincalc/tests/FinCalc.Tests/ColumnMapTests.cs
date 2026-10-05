using ClosedXML.Excel;
using FinCalc.Depreciation;
using FinCalc.Excel;
using Xunit;

namespace FinCalc.Tests;

public class ColumnMapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fincalc-test-{Guid.NewGuid():N}");

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void ColumnMapSet_Load_OverridesOnlySpecifiedLogicals()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "columns.json");
        File.WriteAllText(path, """
            {
              "asset": { "cost": ["仕入価額"] },
              "invoice": { "net": ["請求金額"] }
            }
            """);

        var set = ColumnMapSet.Load(path);

        // 上書きした論理名: 既定の "取得価額" は効かず "仕入価額" が効く
        var row = new SheetReader.RowData(2, new Dictionary<string, string> { ["仕入価額"] = "1,000" });
        Assert.Equal("1,000", set.Asset.Req(row, "cost"));
        // 未上書きの論理名は既定のまま
        var row2 = new SheetReader.RowData(3, new Dictionary<string, string> { ["耐用年数"] = "4" });
        Assert.Equal("4", set.Asset.Req(row2, "life"));
        // invoice 側も独立して上書きされる
        var row3 = new SheetReader.RowData(4, new Dictionary<string, string> { ["請求金額"] = "999" });
        Assert.Equal("999", set.Invoice.Req(row3, "net"));
    }

    [Fact]
    public void DepBatch_WithDepartmentColumns_ProcessesCustomHeaders()
    {
        Directory.CreateDirectory(_dir);
        var input = Path.Combine(_dir, "custom.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("台帳");
            // 部署独自のヘッダ名（既定エイリアスでは拾えない）
            ws.Cell(1, 1).Value = "管理番号";
            ws.Cell(1, 2).Value = "仕入価額";
            ws.Cell(1, 3).Value = "償却年数";
            ws.Cell(1, 4).Value = "償却方法";
            ws.Cell(2, 1).Value = "A-001";
            ws.Cell(2, 2).Value = "1,000,000";
            ws.Cell(2, 3).Value = 10;
            ws.Cell(2, 4).Value = "定額";
            wb.SaveAs(input);
        }

        var map = ColumnMap.Asset.WithOverrides(new Dictionary<string, IReadOnlyList<string>>
        {
            ["name"] = ["管理番号"],
            ["cost"] = ["仕入価額"],
            ["life"] = ["償却年数"],
        });
        var r = new DepreciationBatchProcessor(map)
            .Run(input, null, null, Path.Combine(_dir, "out.xlsx"));

        Assert.Equal(1, r.OkCount);
        Assert.Equal(100_000, r.Summary[0].Year1Expense); // 定額法10年 = 年10万
    }

    [Fact]
    public void DepreciationStrategies_With_LetsYouAddCustomMethod()
    {
        // 独自戦略: 1年で全額償却する仮の方法（既存コードを改変せず差し込めることの検証）
        var custom = new CustomStrategy();
        var strategies = DepreciationStrategies.Default.With(custom);
        var calc = new DepreciationCalculator(strategies);

        var rows = calc.Schedule(500_000, 5, DepreciationMethod.DecliningBalance200);

        Assert.Single(rows);
        Assert.Equal(499_999, rows[0].Expense);

        // 既定側は汚染されていない
        Assert.Equal(250_000,
            DepreciationCalculator.Default.Schedule(1_000_000, 8, DepreciationMethod.DecliningBalance200)[0].Expense);
    }

    private sealed class CustomStrategy : IDepreciationStrategy
    {
        public DepreciationMethod Method => DepreciationMethod.DecliningBalance200;
        public IReadOnlyList<DepreciationEntry> Schedule(long cost, int life, int firstYearMonths, RoundMode round) =>
            new[] { new DepreciationEntry(1, "custom", 1.0m, cost - 1, cost - 1, 1, false) };
    }
}
