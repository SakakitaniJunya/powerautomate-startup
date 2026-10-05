namespace FinCalc.Excel;

/// <summary>
/// Excel 行で検出した異常1件。sheet/row で場所、field で対象論理列、
/// value で入力値、reason で判定理由を持つ。CLI JSON 経由で PAD が参照する。
/// </summary>
public sealed record Anomaly(
    string Sheet, int Row, string Asset, string Field, string? Value, string Reason);

/// <summary>
/// 部署プロファイルに載せる閾値ルール。未設定(null)の項目はチェックしない。
/// ポリシー差し替えはこのレコードを注入するだけでよい（プロセッサ本体は不変）。
/// </summary>
public sealed record AnomalyRules(
    long? MaxCost = null,
    int? MinLife = null,
    int? MaxLife = null,
    long? MaxYear1Expense = null)
{
    /// <summary>ルール未指定時の空ルール（異常はパースエラーのみ）。</summary>
    public static AnomalyRules None { get; } = new();

    /// <summary>パース成功した1行分の値に対し、違反したルールを列挙する。</summary>
    public IEnumerable<(string Field, string Value, string Reason)> Check(
        long cost, int life, long? year1Expense)
    {
        if (MaxCost is { } mc && cost > mc)
            yield return ("cost", cost.ToString(),
                $"取得価額 {cost:N0} が部署上限 {mc:N0} を超えています");
        if (MinLife is { } mn && life < mn)
            yield return ("life", life.ToString(),
                $"耐用年数 {life} が部署下限 {mn} を下回っています");
        if (MaxLife is { } mx && life > mx)
            yield return ("life", life.ToString(),
                $"耐用年数 {life} が部署上限 {mx} を超えています");
        if (MaxYear1Expense is { } my && year1Expense > my)
            yield return ("year1Expense", year1Expense.Value.ToString(),
                $"初年度償却費 {year1Expense:N0} が部署上限 {my:N0} を超えています");
    }
}
