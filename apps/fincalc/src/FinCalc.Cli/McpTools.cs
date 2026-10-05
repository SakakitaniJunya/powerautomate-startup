using System.ComponentModel;
using System.Text.Json;
using ClosedXML.Excel;
using FinCalc;
using FinCalc.Depreciation;
using FinCalc.Excel;
using FinCalc.Tax;
using ModelContextProtocol.Server;

namespace FinCalc.Cli;

/// <summary>MCP ツール群。結果は JSON 文字列で返す（CLI と同じスキーマ）。</summary>
[McpServerToolType]
public static class FinCalcMcpTools
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string ToJson(object o) => JsonSerializer.Serialize(o, Json);

    [McpServerTool(Name = "dep_schedule"), Description(
        "減価償却スケジュールを計算する。method: straight(定額法)|declining(200%定率法)。" +
        "cost=取得価額(円), life=耐用年数(2-86), months=初年度供用月数(1-12, 既定12), round=floor|ceiling|nearest。" +
        "結果は {schedule: [{year, rate, expense, accumulated, bookValueEnd, isRevised}]}")]
    public static string DepSchedule(
        [Description("straight または declining")] string method,
        [Description("取得価額（円）")] long cost,
        [Description("耐用年数")] int life,
        [Description("初年度の事業供用月数")] int months = 12,
        [Description("端数処理: floor|ceiling|nearest")] string round = "floor")
    {
        var m = method.ToLowerInvariant() switch
        {
            "straight" => DepreciationMethod.StraightLine,
            "declining" or "db200" => DepreciationMethod.DecliningBalance200,
            _ => throw new ArgumentException($"method は straight|declining: {method}")
        };
        var rows = new DepreciationCalculator().Schedule(cost, life, m, months, ParseRound(round));
        return ToJson(new { cost, usefulLifeYears = life, method = m.ToString(), schedule = rows });
    }

    [McpServerTool(Name = "rates"), Description("耐用年数から償却率・改定償却率・保証率を参照する")]
    public static string Rates([Description("耐用年数")] int life)
    {
        var db = RateTable.DecliningBalance200(life);
        return ToJson(new
        {
            usefulLifeYears = life,
            straightLineRate = RateTable.StraightLineRate(life),
            decliningBalanceRate = db.Rate,
            revisedRate = db.RevisedRate,
            guaranteeRate = db.GuaranteeRate,
        });
    }

    [McpServerTool(Name = "tax_consumption"), Description("消費税: 税抜金額に税率(10|8)を掛けて税額と税込を返す")]
    public static string TaxConsumption(long net, decimal rate, string round = "floor") =>
        ToJson(new ConsumptionTaxCalculator().AddTax(net, NormRate(rate), ParseRound(round)));

    [McpServerTool(Name = "tax_consumption_net"), Description("消費税: 税込金額から税抜と税額を逆算する")]
    public static string TaxConsumptionNet(long gross, decimal rate, string round = "floor") =>
        ToJson(new ConsumptionTaxCalculator().ExtractTax(gross, NormRate(rate), ParseRound(round)));

    [McpServerTool(Name = "tax_invoice"), Description(
        "適格請求書の消費税を税率別に集計。lines は JSON 配列文字列 '[{\"net\":333,\"rate\":10}]'")]
    public static string TaxInvoice(
        [Description("JSON 配列 [{\"net\":金額,\"rate\":10|8}]")] string lines,
        string round = "floor")
    {
        var parsed = JsonSerializer.Deserialize<List<Line>>(lines, Json)
            ?? throw new ArgumentException("lines の JSON が不正です");
        var r = new ConsumptionTaxCalculator().Invoice(
            parsed.Select(l => (l.Net, NormRate(l.Rate))), ParseRound(round));
        return ToJson(r);
    }

    [McpServerTool(Name = "tax_withholding"), Description(
        "報酬・料金の源泉所得税（復興特別所得税込み: 100万以下10.21% / 超過分20.42%）")]
    public static string TaxWithholding([Description("支払金額（円・税込）")] long amount) =>
        ToJson(new WithholdingTaxCalculator().ForFee(amount));

    [McpServerTool(Name = "tax_corporate"), Description(
        "法人税概算（中小法人: 所得800万以下15% / 超過23.2% + 地方法人税10.3%。住民税・事業税は含まない概算）")]
    public static string TaxCorporate([Description("課税所得（円）")] long income) =>
        ToJson(new CorporateTaxCalculator().Estimate(income));

    [McpServerTool(Name = "excel_dep_batch"), Description(
        "資産台帳 xlsx を一括償却計算し結果 xlsx (summary+schedule) を出力。" +
        "sheet は名前|1始まり番号|*(全シート)。ヘッダ行は自動検出、列名は資産名/asset 等のエイリアス対応")]
    public static string ExcelDepBatch(
        [Description("入力 xlsx のパス")] string input,
        [Description("シート名・番号・*")] string? sheet = "*",
        [Description("ヘッダ行番号（省略時は自動検出）")] int? headerRow = null,
        [Description("出力 xlsx のパス")] string? output = null)
    {
        var r = DepBatch.Run(input, sheet, headerRow, output);
        return ToJson(new { r.OutputPath, r.OkCount, r.ErrorCount, r.Summary });
    }

    [McpServerTool(Name = "excel_invoice"), Description("請求明細 xlsx を税率別に集計し結果 xlsx を出力")]
    public static string ExcelInvoice(
        string input, string? sheet = "*", int? headerRow = null,
        string? output = null, string round = "floor")
    {
        var r = InvoiceBatch.Run(input, sheet, headerRow, output, ParseRound(round));
        return ToJson(r);
    }

    [McpServerTool(Name = "excel_read"), Description("xlsx のシートを JSON 行データとして返す（ヘッダ自動検出）")]
    public static string ExcelRead(string input, string? sheet = null, int? headerRow = null)
    {
        using var wb = new XLWorkbook(input);
        var sheets = SheetReader.TargetSheets(wb, sheet)
            .Select(ws =>
            {
                var rows = SheetReader.Read(ws, headerRow,
                    HeaderMap.AssetColumns.Values.Concat(HeaderMap.InvoiceColumns.Values).SelectMany(a => a));
                return new
                {
                    sheet = rows.SheetName,
                    rows = rows.Rows.Select(x => new { row = x.RowNumber, cells = x.Cells }).ToList()
                };
            }).ToList();
        return ToJson(new { input, sheets });
    }

    [McpServerTool(Name = "excel_init_sample"), Description("複雑フォーマットのサンプル xlsx を生成する")]
    public static string ExcelInitSample(string? output = null)
    {
        var path = output ?? Path.Combine(Directory.GetCurrentDirectory(), "assets.xlsx");
        return ToJson(new { output = SampleWorkbook.CreateAssetLedger(path) });
    }

    private static decimal NormRate(decimal rate) => rate > 1m ? rate / 100m : rate;

    private static RoundMode ParseRound(string s) => s.ToLowerInvariant() switch
    {
        "floor" => RoundMode.Floor,
        "ceiling" or "ceil" => RoundMode.Ceiling,
        "nearest" or "round" => RoundMode.Nearest,
        _ => throw new ArgumentException($"round は floor|ceiling|nearest: {s}")
    };

    private sealed record Line(long Net, decimal Rate);
}
