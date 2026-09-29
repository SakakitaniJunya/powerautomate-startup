using ClosedXML.Excel;
using FinCalc;
using FinCalc.Excel;
using Xunit;

namespace FinCalc.Tests;

public class ExcelBatchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fincalc-test-{Guid.NewGuid():N}");

    public void Dispose() => Directory.Delete(_dir, true);

    private string WriteSampleInput()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "assets.xlsx");
        SampleWorkbook.CreateAssetLedger(path);
        return path;
    }

    [Fact]
    public void DepBatch_ProcessesComplexWorkbook()
    {
        var input = WriteSampleInput();
        var output = Path.Combine(_dir, "out.xlsx");

        var r = DepBatch.Run(input, "資産台帳", null, output);

        Assert.Equal(5, r.OkCount);
        Assert.Equal(0, r.ErrorCount);
        Assert.True(File.Exists(output));

        using var wb = new XLWorkbook(output);
        var sum = wb.Worksheet("summary");
        // PC: 250,000円・4年・定率 → 初年度 125,000
        Assert.Equal(125_000, sum.Cell(2, 7).GetValue<long>());
        // 建物: 50,000,000・50年・定額 → 初年度 1,000,000
        Assert.Equal(1_000_000, sum.Cell(6, 7).GetValue<long>());

        var sch = wb.Worksheet("schedule");
        Assert.True(sch.LastRowUsed()!.RowNumber() > 60); // 4+5+5+8+50 = 72行+ヘッダ
    }

    [Fact]
    public void DepBatch_AllSheets_SkipsNonTargetSheets()
    {
        var input = WriteSampleInput();
        var output = Path.Combine(_dir, "out2.xlsx");

        // "*" で全シート。メモシートは既知ヘッダが無いので読み飛ばされる
        var r = DepBatch.Run(input, "*", null, output);

        Assert.Equal(5, r.OkCount);
    }

    [Fact]
    public void DepBatch_BadRow_GoesToErrorColumn()
    {
        Directory.CreateDirectory(_dir);
        var input = Path.Combine(_dir, "bad.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("台帳");
            ws.Cell(1, 1).Value = "資産名";
            ws.Cell(1, 2).Value = "取得価額";
            ws.Cell(1, 3).Value = "耐用年数";
            ws.Cell(1, 4).Value = "償却方法";
            ws.Cell(2, 1).Value = "壊れた行";
            ws.Cell(2, 2).Value = "あいうえお";
            ws.Cell(2, 3).Value = 5;
            ws.Cell(2, 4).Value = "定額";
            wb.SaveAs(input);
        }

        var r = DepBatch.Run(input, null, null, Path.Combine(_dir, "badout.xlsx"));

        Assert.Equal(0, r.OkCount);
        Assert.Equal(1, r.ErrorCount);
        Assert.NotNull(r.Summary[0].Error);
    }

    [Fact]
    public void InvoiceBatch_MultiSheet_Aggregates()
    {
        var input = WriteSampleInput();
        var output = Path.Combine(_dir, "inv.xlsx");

        // invoice_lines シートだけ対象（資産台帳は net/rate 列を持たないので飛ばされる）
        var r = InvoiceBatch.Run(input, "*", null, output, RoundMode.Floor);

        Assert.Equal(2, r.Groups.Count);
        var g10 = r.Groups.Single(g => g.Rate == 0.10m);
        Assert.Equal(999, g10.NetTotal);
        Assert.Equal(99, g10.TaxTotal);
        Assert.Equal(17, r.Groups.Single(g => g.Rate == 0.08m).TaxTotal);
    }

    [Fact]
    public void SheetReader_AutoDetectsHeaderRow()
    {
        var input = WriteSampleInput();
        using var wb = new XLWorkbook(input);
        var ws = wb.Worksheet("資産台帳");

        var rows = SheetReader.Read(ws, null,
            HeaderMap.AssetColumns.Values.SelectMany(a => a));

        Assert.Equal(5, rows.Rows.Count);
        Assert.Equal("PC", rows.Rows[0].Cells["資産名"]);
        Assert.Equal(4, rows.Rows[0].RowNumber); // ヘッダ3行目 → データは4行目から
    }
}
