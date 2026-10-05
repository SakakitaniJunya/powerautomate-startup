using ClosedXML.Excel;
using FinCalc.Tax;

namespace FinCalc.Excel;

/// <summary>請求明細 Excel を税率別に集計し、結果シートを出力する。</summary>
public sealed class InvoiceBatchProcessor
{
    private readonly ColumnMap _columns;
    private readonly ConsumptionTaxCalculator _tax;

    public InvoiceBatchProcessor(
        ColumnMap? columns = null, ConsumptionTaxCalculator? tax = null)
    {
        _columns = columns ?? ColumnMap.Invoice;
        _tax = tax ?? new ConsumptionTaxCalculator();
    }

    public ConsumptionTaxCalculator.InvoiceResult Run(
        string inputPath, string? sheet, int? headerRow, string? outputPath,
        RoundMode round = RoundMode.Floor)
    {
        outputPath ??= Path.ChangeExtension(inputPath, ".tax-result.xlsx");

        var lines = new List<(long net, decimal rate)>();
        using (var wb = new XLWorkbook(inputPath))
        {
            foreach (var ws in SheetReader.TargetSheets(wb, sheet))
            {
                SheetReader.SheetRows rows;
                try
                {
                    rows = SheetReader.Read(ws, headerRow, _columns.AllHeaders);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                foreach (var row in rows.Rows)
                {
                    var net = HeaderMap.ParseMoney(_columns.Req(row, "net"));
                    var rateRaw = decimal.Parse(_columns.Req(row, "rate"));
                    var rate = rateRaw > 1m ? rateRaw / 100m : rateRaw;
                    lines.Add((net, rate));
                }
            }
        }

        if (lines.Count == 0)
            throw new ArgumentException("明細行が0件です。net/金額, rate/税率 の列を持つシートを確認してください");

        var result = _tax.Invoice(lines, round);

        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("invoice_tax");
            ws.Cell(1, 1).InsertData(new[] { "rate", "netTotal", "taxTotal" });
            var r = 2;
            foreach (var g in result.Groups)
            {
                ws.Cell(r, 1).Value = g.Rate;
                ws.Cell(r, 2).Value = g.NetTotal;
                ws.Cell(r, 3).Value = g.TaxTotal;
                r++;
            }
            ws.Cell(r, 1).Value = "合計";
            ws.Cell(r, 2).Value = result.NetTotal;
            ws.Cell(r, 3).Value = result.TaxTotal;
            ws.Cell(r, 4).Value = result.GrossTotal;
            ws.Columns().AdjustToContents();
            wb.SaveAs(outputPath);
        }

        return result;
    }
}

/// <summary>既定構成での一括実行ショートカット（後方互換ファサード）。</summary>
public static class InvoiceBatch
{
    public static ConsumptionTaxCalculator.InvoiceResult Run(
        string inputPath, string? sheet, int? headerRow, string? outputPath,
        RoundMode round = RoundMode.Floor, ColumnMap? columns = null) =>
        new InvoiceBatchProcessor(columns).Run(inputPath, sheet, headerRow, outputPath, round);
}
