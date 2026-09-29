using ClosedXML.Excel;
using FinCalc.Depreciation;

namespace FinCalc.Excel;

/// <summary>
/// 資産台帳 Excel を一括処理し、償却スケジュール付きの結果ワークブックを出力する。
/// 1行の失敗で全体を止めず、エラーは summary シートの error 列に記録する。
/// </summary>
public static class DepBatch
{
    public sealed record SummaryRow(
        string Sheet, int Row, string Asset, long Cost, int Life, string Method,
        long? Year1Expense, long? TotalExpense, long? FinalBookValue, string? Error);

    public sealed record Result(IReadOnlyList<SummaryRow> Summary, int OkCount, int ErrorCount, string OutputPath);

    public static Result Run(string inputPath, string? sheet, int? headerRow, string? outputPath)
    {
        outputPath ??= Path.ChangeExtension(inputPath, ".result.xlsx");

        var summary = new List<SummaryRow>();
        var schedules = new List<(string Asset, DepreciationEntry E)>();

        using (var wb = new XLWorkbook(inputPath))
        {
            foreach (var ws in SheetReader.TargetSheets(wb, sheet))
            {
                SheetReader.SheetRows rows;
                try
                {
                    rows = SheetReader.Read(ws, headerRow,
                        HeaderMap.AssetColumns.Values.SelectMany(a => a));
                }
                catch (ArgumentException)
                {
                    continue; // 対象外シート（ヘッダ無し）は読み飛ばす
                }

                foreach (var row in rows.Rows)
                {
                    var name = HeaderMap.Get(row, HeaderMap.AssetColumns, "name") ?? $"行{row.RowNumber}";
                    try
                    {
                        var cost = HeaderMap.ParseMoney(HeaderMap.Req(row, HeaderMap.AssetColumns, "cost"));
                        var life = HeaderMap.ParseInt(HeaderMap.Req(row, HeaderMap.AssetColumns, "life"));
                        var method = HeaderMap.ParseMethod(HeaderMap.Req(row, HeaderMap.AssetColumns, "method"));
                        var months = HeaderMap.Get(row, HeaderMap.AssetColumns, "months") is { } m
                            ? HeaderMap.ParseInt(m) : 12;
                        var round = HeaderMap.ParseRound(HeaderMap.Get(row, HeaderMap.AssetColumns, "round"));

                        var sched = DepreciationCalculator.Schedule(cost, life, method, months, round);
                        foreach (var e in sched) schedules.Add((name, e));

                        summary.Add(new(rows.SheetName, row.RowNumber, name, cost, life,
                            method.ToString(), sched[0].Expense, sched.Sum(e => e.Expense),
                            sched[^1].BookValueEnd, null));
                    }
                    catch (Exception ex)
                    {
                        summary.Add(new(rows.SheetName, row.RowNumber, name, 0, 0, "",
                            null, null, null, ex.Message));
                    }
                }
            }
        }

        WriteResult(outputPath, summary, schedules);

        return new(summary, summary.Count(s => s.Error is null), summary.Count(s => s.Error is not null), outputPath);
    }

    private static void WriteResult(
        string outputPath, List<SummaryRow> summary, List<(string Asset, DepreciationEntry E)> schedules)
    {
        using var wb = new XLWorkbook();

        var wsSum = wb.AddWorksheet("summary");
        wsSum.Cell(1, 1).InsertData(new[] { "sheet", "row", "asset", "cost", "life", "method",
            "year1Expense", "totalExpense", "finalBookValue", "error" });
        var r = 2;
        foreach (var s in summary)
        {
            wsSum.Cell(r, 1).Value = s.Sheet;
            wsSum.Cell(r, 2).Value = s.Row;
            wsSum.Cell(r, 3).Value = s.Asset;
            wsSum.Cell(r, 4).Value = s.Cost;
            wsSum.Cell(r, 5).Value = s.Life;
            wsSum.Cell(r, 6).Value = s.Method;
            if (s.Year1Expense is { } y1) wsSum.Cell(r, 7).Value = y1;
            if (s.TotalExpense is { } t) wsSum.Cell(r, 8).Value = t;
            if (s.FinalBookValue is { } fb) wsSum.Cell(r, 9).Value = fb;
            if (s.Error is { } e) wsSum.Cell(r, 10).Value = e;
            r++;
        }
        wsSum.Columns().AdjustToContents();

        var wsSch = wb.AddWorksheet("schedule");
        wsSch.Cell(1, 1).InsertData(new[] { "asset", "year", "method", "rate",
            "expense", "accumulated", "bookValueEnd", "isRevised" });
        r = 2;
        foreach (var (asset, e) in schedules)
        {
            wsSch.Cell(r, 1).Value = asset;
            wsSch.Cell(r, 2).Value = e.Year;
            wsSch.Cell(r, 3).Value = e.Method;
            wsSch.Cell(r, 4).Value = e.Rate;
            wsSch.Cell(r, 5).Value = e.Expense;
            wsSch.Cell(r, 6).Value = e.Accumulated;
            wsSch.Cell(r, 7).Value = e.BookValueEnd;
            wsSch.Cell(r, 8).Value = e.IsRevised;
            r++;
        }
        wsSch.Columns().AdjustToContents();

        wb.SaveAs(outputPath);
    }
}
