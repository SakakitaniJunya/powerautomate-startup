using ClosedXML.Excel;

namespace FinCalc.Excel;

/// <summary>
/// 複雑な Excel シートを行データに読むための読み取り層。
/// - ヘッダ行は自動検出（先頭10行以内で既知の別名列が最も多く並んだ行）または --header-row で指定
/// - 列名はエイリアス表で正規化（前後空白・全半角差・大小文字を吸収）
/// - 数値セル・文字列セル（カンマ/円/¥付き）両対応
/// </summary>
public static class SheetReader
{
    /// <summary>1シート分の行データ（ヘッダ名→文字列値）。行番号は Excel 上の行番号。</summary>
    public sealed record SheetRows(string SheetName, IReadOnlyList<RowData> Rows);
    public sealed record RowData(int RowNumber, IReadOnlyDictionary<string, string> Cells);

    /// <summary>対象シートを列挙。sheet が null なら先頭シート、"*" なら全シート、数字なら1始まりの位置。</summary>
    public static IEnumerable<IXLWorksheet> TargetSheets(XLWorkbook wb, string? sheet)
    {
        if (string.IsNullOrEmpty(sheet))
            return wb.Worksheets.Take(1);
        if (sheet == "*" || sheet.Equals("all", StringComparison.OrdinalIgnoreCase))
            return wb.Worksheets;
        if (int.TryParse(sheet, out var idx))
        {
            var ws = wb.Worksheets.ElementAtOrDefault(idx - 1)
                ?? throw new ArgumentException($"シート位置 {idx} が範囲外です（{wb.Worksheets.Count} シート）");
            return new[] { ws };
        }
        var named = wb.Worksheets.FirstOrDefault(w => w.Name == sheet)
            ?? throw new ArgumentException($"シート '{sheet}' が見つかりません");
        return new[] { named };
    }

    /// <summary>ヘッダ行を自動検出して行データを返す。aliases に一致したヘッダ数で採点。</summary>
    public static SheetRows Read(IXLWorksheet ws, int? headerRow, IEnumerable<string> knownHeaders)
    {
        var known = knownHeaders.Select(Normalize).ToHashSet();
        var header = headerRow ?? DetectHeaderRow(ws, known);

        var headers = ws.Row(header).CellsUsed()
            .ToDictionary(c => c.Address.ColumnNumber, c => c.GetString().Trim());

        var rows = new List<RowData>();
        foreach (var row in ws.Rows(header + 1, ws.LastRowUsed()?.RowNumber() ?? header))
        {
            if (!row.CellsUsed().Any()) continue;
            var cells = headers
                .Where(h => row.Cell(h.Key).GetString().Trim() != "")
                .ToDictionary(h => h.Value, h => row.Cell(h.Key).GetString().Trim());
            if (cells.Count > 0)
                rows.Add(new RowData(row.RowNumber(), cells));
        }
        return new SheetRows(ws.Name, rows);
    }

    private static int DetectHeaderRow(IXLWorksheet ws, HashSet<string> known)
    {
        var last = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 1, 10);
        var best = 1; var bestScore = 0;
        for (var r = 1; r <= last; r++)
        {
            var score = ws.Row(r).CellsUsed()
                .Select(c => Normalize(c.GetString()))
                .Count(known.Contains);
            if (score > bestScore) { bestScore = score; best = r; }
        }
        if (bestScore == 0)
            throw new ArgumentException($"シート '{ws.Name}' の先頭10行に既知のヘッダ列がありません。--header-row で指定してください");
        return best;
    }

    public static string Normalize(string s) =>
        s.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("　", "");
}
