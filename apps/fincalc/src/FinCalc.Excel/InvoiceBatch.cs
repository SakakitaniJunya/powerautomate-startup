using ClosedXML.Excel;
using FinCalc.Tax;

namespace FinCalc.Excel;

/// <summary>請求明細 Excel を税率別に集計し、結果シートを出力する。</summary>
public static class InvoiceBatch
{
    public static ConsumptionTax.InvoiceResult Run(
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
                    rows = SheetReader.Read(ws, headerRow,
                        HeaderMap.InvoiceColumns.Values.SelectMany(a => a));
                }
                catch (ArgumentException)
                {
                    continue;
                }

                foreach (var row in rows.Rows)
                {
                    var net = HeaderMap.ParseMoney(HeaderMap.Req(row, HeaderMap.InvoiceColumns, "net"));
                    var rateRaw = decimal.Parse(HeaderMap.Req(row, HeaderMap.InvoiceColumns, "rate"));
                    var rate = rateRaw > 1m ? rateRaw / 100m : rateRaw;
                    lines.Add((net, rate));
                }
            }
        }

        if (lines.Count == 0)
            throw new ArgumentException("明細行が0件です。net/金額, rate/税率 の列を持つシートを確認してください");

        var result = ConsumptionTax.Invoice(lines, round);

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
