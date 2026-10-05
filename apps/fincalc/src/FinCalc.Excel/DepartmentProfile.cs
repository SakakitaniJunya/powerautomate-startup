using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinCalc.Excel;

/// <summary>
/// 部署ごとの処理設定。部署の Excel フォーマット差（列対応）・異常判定ルール・
/// 異常通知先の部長メールアドレスを1ファイルに束ねる。
///
/// JSON 形式:
/// {
///   "code": "dept-a",
///   "name": "営業部",
///   "managerEmail": "manager@example.com",
///   "columns": { "asset": { "cost": ["仕入価額"] }, "invoice": { ... } },
///   "anomaly": { "maxCost": 5000000, "minLife": 2, "maxLife": 50 }
/// }
///
/// columns 省略時は既定列対応、anomaly 省略時はパースエラーのみを異常とみなす。
/// </summary>
public sealed record DepartmentProfile(
    string Code,
    string Name,
    string? ManagerEmail,
    ColumnMapSet Columns,
    AnomalyRules Anomaly)
{
    /// <summary>部署プロファイル JSON を読み込む。必須項目欠落・形式不正は例外。</summary>
    public static DepartmentProfile Load(string path)
    {
        if (!File.Exists(path))
            throw new ArgumentException($"部署プロファイルがありません: {path}");

        var doc = JsonSerializer.Deserialize<ProfileDoc>(
            File.ReadAllText(path),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
            })
            ?? throw new ArgumentException($"部署プロファイル JSON が空です: {path}");

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(doc.Code)) missing.Add("code");
        if (string.IsNullOrWhiteSpace(doc.Name)) missing.Add("name");
        if (doc.Columns is not null)
        {
            foreach (var (section, map) in doc.Columns)
            {
                if (section is not ("asset" or "invoice" or "delay"))
                    throw new ArgumentException(
                        $"columns.{section} は未定義のセクションです（asset|invoice|delay のみ）");
                if (map is null) missing.Add($"columns.{section}");
            }
        }
        if (missing.Count > 0)
            throw new ArgumentException(
                $"部署プロファイルに必須項目がありません: {string.Join(", ", missing)} ({path})");

        return new(
            doc.Code!,
            doc.Name!,
            string.IsNullOrWhiteSpace(doc.ManagerEmail) ? null : doc.ManagerEmail,
            doc.Columns is null
                ? ColumnMapSet.Default
                : ColumnMapSet.FromSections(doc.Columns),
            doc.Anomaly is { } ad ? (AnomalyRules)ad : AnomalyRules.None);
    }

    private sealed class ProfileDoc
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? ManagerEmail { get; set; }
        public Dictionary<string, Dictionary<string, string[]>>? Columns { get; set; }
        public AnomalyRulesDoc? Anomaly { get; set; }
    }

    private sealed class AnomalyRulesDoc
    {
        public long? MaxCost { get; set; }
        public int? MinLife { get; set; }
        public int? MaxLife { get; set; }
        public long? MaxYear1Expense { get; set; }

        public static implicit operator AnomalyRules(AnomalyRulesDoc d) =>
            new(d.MaxCost, d.MinLife, d.MaxLife, d.MaxYear1Expense);
    }
}
