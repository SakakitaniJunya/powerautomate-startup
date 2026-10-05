using System.Text.Json;

namespace FinCalc.Excel;

/// <summary>
/// 部署・案件ごとに異なるヘッダ名を「論理名 → 許容ヘッダ名」の対応表として保持する。
/// 既定のエイリアスは <see cref="HeaderMap"/> が持ち、部署固有の表記は
/// JSON (<see cref="ColumnMapSet.Load"/>) で上書きできる（コード変更不要）。
/// </summary>
public sealed class ColumnMap
{
    private readonly Dictionary<string, string[]> _aliases;

    public ColumnMap(IReadOnlyDictionary<string, IReadOnlyList<string>> aliases) =>
        _aliases = aliases.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());

    /// <summary>資産台帳（償却バッチ）の既定列対応。</summary>
    public static ColumnMap Asset { get; } = new(HeaderMap.AssetColumns.ToDictionary(
        kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value));

    /// <summary>請求明細（消費税バッチ）の既定列対応。</summary>
    public static ColumnMap Invoice { get; } = new(HeaderMap.InvoiceColumns.ToDictionary(
        kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value));

    /// <summary>ヘッダ行検出用に、全別名を列挙する。</summary>
    public IEnumerable<string> AllHeaders => _aliases.Values.SelectMany(a => a);

    /// <summary>対応表を別セットで上書きした新しい ColumnMap を返す（元は変更しない）。</summary>
    public ColumnMap WithOverrides(IReadOnlyDictionary<string, IReadOnlyList<string>> overrides)
    {
        var merged = new Dictionary<string, IReadOnlyList<string>>(_aliases
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value));
        foreach (var (logical, aliases) in overrides)
            merged[logical] = aliases;
        return new ColumnMap(merged);
    }

    /// <summary>行データから論理名に対応するセル値を取る。無ければ null。</summary>
    public string? Get(SheetReader.RowData row, string logical)
    {
        if (!_aliases.TryGetValue(logical, out var names))
            throw new ArgumentException($"論理列 '{logical}' はこの ColumnMap に未定義です");
        var wanted = names.Select(SheetReader.Normalize).ToHashSet();
        foreach (var (header, value) in row.Cells)
        {
            if (wanted.Contains(SheetReader.Normalize(header)))
                return value;
        }
        return null;
    }

    /// <summary>必須版。列が無ければ行番号つきで例外。</summary>
    public string Req(SheetReader.RowData row, string logical) =>
        Get(row, logical)
        ?? throw new FormatException(
            $"行{row.RowNumber}: 列 '{logical}'（{string.Join("/", _aliases[logical])}）が見つかりません");
}

/// <summary>資産台帳用と請求明細用の ColumnMap の組。部署別 JSON から読み込む。</summary>
public sealed record ColumnMapSet(ColumnMap Asset, ColumnMap Invoice)
{
    public static ColumnMapSet Default { get; } = new(ColumnMap.Asset, ColumnMap.Invoice);

    /// <summary>
    /// 部署別の列対応 JSON を読む。形式:
    /// { "asset": { "cost": ["仕入価額", ...] }, "invoice": { "net": [...] } }
    /// 指定した論理名だけ既定エイリアスを置き換える。未定義の論理名も追加可能。
    /// </summary>
    public static ColumnMapSet Load(string path)
    {
        var doc = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string[]>>>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip })
            ?? throw new ArgumentException($"列対応 JSON が空です: {path}");

        return new(
            doc.TryGetValue("asset", out var a) ? ColumnMap.Asset.WithOverrides(ToMap(a)) : ColumnMap.Asset,
            doc.TryGetValue("invoice", out var i) ? ColumnMap.Invoice.WithOverrides(ToMap(i)) : ColumnMap.Invoice);
    }

    private static Dictionary<string, IReadOnlyList<string>> ToMap(Dictionary<string, string[]> d) =>
        d.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value);
}
