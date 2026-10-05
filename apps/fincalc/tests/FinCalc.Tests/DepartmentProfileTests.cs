using ClosedXML.Excel;
using FinCalc.Excel;
using Xunit;

namespace FinCalc.Tests;

public class DepartmentProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fincalc-test-{Guid.NewGuid():N}");

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private string WriteProfile(string json)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "dept.json");
        File.WriteAllText(path, json);
        return path;
    }

    private string WriteLedger()
    {
        Directory.CreateDirectory(_dir);
        var input = Path.Combine(_dir, "ledger.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("台帳");
        ws.Cell(1, 1).Value = "管理番号";
        ws.Cell(1, 2).Value = "仕入価額";
        ws.Cell(1, 3).Value = "償却年数";
        ws.Cell(1, 4).Value = "償却方法";
        // 正常行
        ws.Cell(2, 1).Value = "A-001";
        ws.Cell(2, 2).Value = "1,000,000";
        ws.Cell(2, 3).Value = 10;
        ws.Cell(2, 4).Value = "定額";
        // 異常行: cost が上限 5,000,000 超過
        ws.Cell(3, 1).Value = "A-002";
        ws.Cell(3, 2).Value = "8,000,000";
        ws.Cell(3, 3).Value = 10;
        ws.Cell(3, 4).Value = "定額";
        // 異常行: life が下限 2 未満
        ws.Cell(4, 1).Value = "A-003";
        ws.Cell(4, 2).Value = "100,000";
        ws.Cell(4, 3).Value = 1;
        ws.Cell(4, 4).Value = "定額";
        // パース不能行: cost が数値でない
        ws.Cell(5, 1).Value = "A-004";
        ws.Cell(5, 2).Value = "未入力";
        ws.Cell(5, 3).Value = 10;
        ws.Cell(5, 4).Value = "定額";
        wb.SaveAs(input);
        return input;
    }

    private const string ProfileJson = """
        {
          "code": "dept-a",
          "name": "営業部",
          "managerEmail": "boss@example.com",
          "columns": {
            "asset": {
              "name": ["管理番号"], "cost": ["仕入価額"],
              "life": ["償却年数"], "method": ["償却方法"]
            }
          },
          "anomaly": { "maxCost": 5000000, "minLife": 2, "maxLife": 50 }
        }
        """;

    [Fact]
    public void Load_ReadsDepartmentIdentityColumnsAndRules()
    {
        var p = DepartmentProfile.Load(WriteProfile(ProfileJson));

        Assert.Equal("dept-a", p.Code);
        Assert.Equal("営業部", p.Name);
        Assert.Equal("boss@example.com", p.ManagerEmail);
        Assert.Equal(5_000_000, p.Anomaly.MaxCost);
        Assert.Equal(2, p.Anomaly.MinLife);

        // 列対応が部署フォーマットを拾う
        var row = new SheetReader.RowData(2, new Dictionary<string, string> { ["仕入価額"] = "100" });
        Assert.Equal("100", p.Columns.Asset.Req(row, "cost"));
    }

    [Fact]
    public void Load_MissingRequired_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DepartmentProfile.Load(WriteProfile("""{"columns": {"asset": {}}}""")));
    }

    [Fact]
    public void Load_MissingFile_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            DepartmentProfile.Load(Path.Combine(_dir, "nope.json")));

    [Fact]
    public void Load_UnknownColumnSection_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            DepartmentProfile.Load(WriteProfile(
                """{"code":"x","name":"x","columns":{"bogus":{"cost":["x"]}}}""")));

    [Fact]
    public void Load_MinimalProfile_UsesDefaults()
    {
        var p = DepartmentProfile.Load(WriteProfile("""{"code":"x","name":"総務部"}"""));

        Assert.Null(p.ManagerEmail);
        Assert.Same(AnomalyRules.None, p.Anomaly);
        // 既定列対応が効く
        var row = new SheetReader.RowData(2, new Dictionary<string, string> { ["取得価額"] = "100" });
        Assert.Equal("100", p.Columns.Asset.Req(row, "cost"));
    }

    [Fact]
    public void DepBatch_WithProfile_DetectsRuleViolationsAndParseErrors()
    {
        var p = DepartmentProfile.Load(WriteProfile(ProfileJson));
        var input = WriteLedger();

        var r = new DepreciationBatchProcessor(p.Columns.Asset, anomalyRules: p.Anomaly)
            .Run(input, null, null, Path.Combine(_dir, "out.xlsx"));

        Assert.Equal(3, r.OkCount);   // 閾値違反行も計算自体は成功
        Assert.Equal(1, r.ErrorCount); // "未入力" のみエラー

        // 異常: cost超過 + life不足 + パースエラー = 3件
        Assert.Equal(3, r.Anomalies.Count);
        Assert.Contains(r.Anomalies, a => a.Asset == "A-002" && a.Field == "cost" && a.Value == "8000000");
        Assert.Contains(r.Anomalies, a => a.Asset == "A-003" && a.Field == "life");
        Assert.Contains(r.Anomalies, a => a.Asset == "A-004" && a.Field == "row");

        // anomalies シートが結果ブックに出る
        using var wb = new XLWorkbook(r.OutputPath);
        Assert.Contains(wb.Worksheets, w => w.Name == "anomalies");
    }

    [Fact]
    public void DepBatch_WithoutRules_OnlyErrorsAreAnomalies()
    {
        var input = WriteLedger();
        // プロファイル無し: 既定列対応では部署ヘッダを拾えないので、
        // 部署列対応だけ使いルールは None を注入
        var map = ColumnMap.Asset.WithOverrides(new Dictionary<string, IReadOnlyList<string>>
        {
            ["name"] = ["管理番号"], ["cost"] = ["仕入価額"],
            ["life"] = ["償却年数"], ["method"] = ["償却方法"],
        });
        var r = new DepreciationBatchProcessor(map, anomalyRules: AnomalyRules.None)
            .Run(input, null, null, Path.Combine(_dir, "out2.xlsx"));

        Assert.Equal(3, r.OkCount);
        Assert.Single(r.Anomalies); // パースエラーの A-004 のみ
        Assert.Equal("row", r.Anomalies[0].Field);
    }

    [Fact]
    public void DepBatch_CleanFile_NoAnomaliesNoSheet()
    {
        Directory.CreateDirectory(_dir);
        var input = Path.Combine(_dir, "clean.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("台帳");
            ws.Cell(1, 1).Value = "資産名";
            ws.Cell(1, 2).Value = "取得価額";
            ws.Cell(1, 3).Value = "耐用年数";
            ws.Cell(1, 4).Value = "償却方法";
            ws.Cell(2, 1).Value = "PC";
            ws.Cell(2, 2).Value = "300,000";
            ws.Cell(2, 3).Value = 4;
            ws.Cell(2, 4).Value = "定額";
            wb.SaveAs(input);
        }

        var r = new DepreciationBatchProcessor(anomalyRules: new AnomalyRules(MaxCost: 1_000_000))
            .Run(input, null, null, Path.Combine(_dir, "clean-out.xlsx"));

        Assert.Empty(r.Anomalies);
        using var wb2 = new XLWorkbook(r.OutputPath);
        Assert.DoesNotContain(wb2.Worksheets, w => w.Name == "anomalies");
    }
}
