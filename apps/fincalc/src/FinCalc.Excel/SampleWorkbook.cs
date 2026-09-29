using ClosedXML.Excel;

namespace FinCalc.Excel;

/// <summary>
/// 複雑なフォーマットのサンプルワークブックを生成する。
/// タイトル行・余計な列・対象外シートを含み、自動ヘッダ検出の動作確認にも使える。
/// </summary>
public static class SampleWorkbook
{
    public static string CreateAssetLedger(string outputPath)
    {
        using var wb = new XLWorkbook();

        // シート1: 資産台帳（1行目タイトル、3行目ヘッダ、余計な列あり）
        var ws = wb.AddWorksheet("資産台帳");
        ws.Cell(1, 1).Value = "固定資産台帳 2026年度（サンプル）";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = "※ 償却方法は 定額/定率 または straight/declining で記入";

        var headers = new[] { "管理番号", "取得日", "資産名", "取得価額", "耐用年数", "償却方法", "供用月数", "端数処理", "メモ" };
        for (var c = 0; c < headers.Length; c++)
        {
            ws.Cell(3, c + 1).Value = headers[c];
            ws.Cell(3, c + 1).Style.Font.Bold = true;
        }

        var rows = new object?[,]
        {
            { "A-001", new DateTime(2026, 4, 1), "PC", 250_000, 4, "定率", 12, "切捨", "" },
            { "A-002", new DateTime(2026, 9, 15), "複合機", 600_000, 5, "定率", 6, "", "10月供用開始" },
            { "A-003", new DateTime(2026, 4, 1), "サーバー", 2_000_000, 5, "定額", 12, "", "" },
            { "A-004", new DateTime(2026, 6, 20), "応接セット", 500_000, 8, "declining", 9, "floor", "" },
            { "A-005", new DateTime(2026, 4, 1), "建物", 50_000_000, 50, "定額", 12, "", "建物は定額法のみ" },
        };
        for (var rr = 0; rr < rows.GetLength(0); rr++)
            for (var cc = 0; cc < rows.GetLength(1); cc++)
                ws.Cell(4 + rr, cc + 1).Value = XLCellValue.FromObject(rows[rr, cc]);
        ws.Columns().AdjustToContents();

        // シート2: 請求明細（英語ヘッダ、1行目ヘッダ）
        var ws2 = wb.AddWorksheet("invoice_lines");
        ws2.Cell(1, 1).Value = "net";
        ws2.Cell(1, 2).Value = "rate";
        ws2.Cell(2, 1).Value = 333; ws2.Cell(2, 2).Value = 10;
        ws2.Cell(3, 1).Value = 333; ws2.Cell(3, 2).Value = 10;
        ws2.Cell(4, 1).Value = 333; ws2.Cell(4, 2).Value = 10;
        ws2.Cell(5, 1).Value = 111; ws2.Cell(5, 2).Value = 8;
        ws2.Cell(6, 1).Value = 111; ws2.Cell(6, 2).Value = 8;
        ws2.Columns().AdjustToContents();

        // シート3: 対象外（ヘッダ検出で読み飛ばされることを確認するためのメモシート）
        var ws3 = wb.AddWorksheet("メモ");
        ws3.Cell(1, 1).Value = "このシートは計算対象外。--sheet * で全シート処理しても読み飛ばされます。";

        wb.SaveAs(outputPath);
        return outputPath;
    }
}
