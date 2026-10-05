using ClosedXML.Excel;
using FinCalc.Excel;

namespace FinCalc.Cli.Commands;

/// <summary>excel dep-batch — 資産台帳 xlsx を一括償却計算。</summary>
public sealed class ExcelDepBatchCommand : ICommand
{
    public string Name => "excel dep-batch";
    public string Usage => "excel dep-batch       --input assets.xlsx [--sheet 名|番号|*] [--header-row N] [--output out.xlsx] [--columns map.json]";

    public object Run(CommandArgs a)
    {
        var input = a.Req("input");
        var r = new DepreciationBatchProcessor(a.LoadColumns().Asset)
            .Run(input, a.Opt("sheet"), a.OptInt("header-row"), a.Opt("output"));
        return new { input, output = r.OutputPath, r.OkCount, r.ErrorCount, summary = r.Summary };
    }
}

/// <summary>excel invoice — 請求明細 xlsx を税率別に集計。</summary>
public sealed class ExcelInvoiceCommand : ICommand
{
    public string Name => "excel invoice";
    public string Usage => "excel invoice         --input lines.xlsx [--sheet 名|番号|*] [--header-row N] [--output out.xlsx] [--round ...] [--columns map.json]";

    public object Run(CommandArgs a)
    {
        var input = a.Req("input");
        var output = a.Opt("output") ?? Path.ChangeExtension(input, ".tax-result.xlsx");
        var r = new InvoiceBatchProcessor(a.LoadColumns().Invoice)
            .Run(input, a.Opt("sheet"), a.OptInt("header-row"), output, a.OptRound());
        return new { input, output, r.Groups, r.NetTotal, r.TaxTotal, r.GrossTotal };
    }
}

/// <summary>excel read — xlsx の行データを JSON で返す（フォーマット確認用）。</summary>
public sealed class ExcelReadCommand : ICommand
{
    public string Name => "excel read";
    public string Usage => "excel read            --input x.xlsx [--sheet 名|番号|*] [--header-row N] [--columns map.json]";

    public object Run(CommandArgs a)
    {
        var input = a.Req("input");
        var columns = a.LoadColumns();
        var known = columns.Asset.AllHeaders.Concat(columns.Invoice.AllHeaders);
        using var wb = new XLWorkbook(input);
        var sheets = SheetReader.TargetSheets(wb, a.Opt("sheet"))
            .Select(ws =>
            {
                var rows = SheetReader.Read(ws, a.OptInt("header-row"), known);
                return new
                {
                    sheet = rows.SheetName,
                    rows = rows.Rows.Select(x => new { row = x.RowNumber, cells = x.Cells }).ToList()
                };
            })
            .ToList();
        return new { input, sheets };
    }
}

/// <summary>excel init-sample — サンプル資産台帳 xlsx を生成。</summary>
public sealed class ExcelInitSampleCommand : ICommand
{
    public string Name => "excel init-sample";
    public string Usage => "excel init-sample     [--output assets.xlsx]";

    public object Run(CommandArgs a)
    {
        var output = a.Opt("output") ?? Path.Combine(Directory.GetCurrentDirectory(), "assets.xlsx");
        return new { output = SampleWorkbook.CreateAssetLedger(output) };
    }
}
