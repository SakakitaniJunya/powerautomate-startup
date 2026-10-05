namespace FinCalc.Excel;

/// <summary>列名エイリアス解決とセル値のパース。</summary>
public static class HeaderMap
{
    // 論理名 → 許容するヘッダ名（Normalize 後で比較）
    public static readonly Dictionary<string, string[]> AssetColumns = new()
    {
        ["name"] = ["資産名", "asset", "name", "資産", "資産コード"],
        ["cost"] = ["取得価額", "取得価格", "cost", "acquisitioncost", "価額"],
        ["life"] = ["耐用年数", "life", "usefullife", "耐用"],
        ["method"] = ["償却方法", "method", "方法"],
        ["months"] = ["供用月数", "months", "初年度月数", "月数"],
        ["round"] = ["端数処理", "round", "rounding"],
    };

    public static readonly Dictionary<string, string[]> InvoiceColumns = new()
    {
        ["net"] = ["net", "金額", "税抜金額", "税抜", "amount"],
        ["rate"] = ["rate", "税率", "消費税率"],
    };

    public static string? Get(SheetReader.RowData row, Dictionary<string, string[]> map, string logical)
    {
        var aliases = map[logical].Select(SheetReader.Normalize).ToHashSet();
        foreach (var (header, value) in row.Cells)
        {
            if (aliases.Contains(SheetReader.Normalize(header)))
                return value;
        }
        return null;
    }

    public static string Req(SheetReader.RowData row, Dictionary<string, string[]> map, string logical) =>
        Get(row, map, logical)
        ?? throw new FormatException($"行{row.RowNumber}: 列 '{logical}'（{string.Join("/", map[logical])}）が見つかりません");

    /// <summary>「1,000,000円」「¥1,000」「1,000,000.5」等を long に。</summary>
    public static long ParseMoney(string s)
    {
        var cleaned = s.Replace(",", "").Replace("円", "").Replace("¥", "").Replace("\\", "").Trim();
        return decimal.TryParse(cleaned, out var v)
            ? (long)decimal.Round(v, 0)
            : throw new FormatException($"金額として解釈できません: '{s}'");
    }

    public static int ParseInt(string s) =>
        int.TryParse(s.Trim(), out var v) ? v
        : throw new FormatException($"整数として解釈できません: '{s}'");

    public static Depreciation.DepreciationMethod ParseMethod(string s) =>
        SheetReader.Normalize(s) switch
        {
            "straight" or "定額" or "定額法" or "sl" => Depreciation.DepreciationMethod.StraightLine,
            "declining" or "db200" or "db" or "定率" or "定率法" or "200%定率法"
                => Depreciation.DepreciationMethod.DecliningBalance200,
            var m => throw new FormatException($"償却方法として解釈できません: '{s}'（straight/定額 or declining/定率）")
        };

    public static RoundMode ParseRound(string? s) =>
        string.IsNullOrEmpty(s) ? RoundMode.Floor
        : SheetReader.Normalize(s) switch
        {
            "floor" or "切捨" or "切り捨て" or "切捨て" => RoundMode.Floor,
            "ceiling" or "ceil" or "切上" or "切り上げ" or "切上げ" => RoundMode.Ceiling,
            "nearest" or "round" or "四捨五入" => RoundMode.Nearest,
            _ => throw new FormatException($"端数処理として解釈できません: '{s}'（floor/ceiling/nearest）")
        };
}
