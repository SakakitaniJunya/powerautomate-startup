using System.Text.RegularExpressions;

namespace PadKit.Flow;

/// <summary>PAD フローテキストをバージョン依存の構文落とし穴について検査する。
/// データ駆動ルール (rules/*.json) と構造ルール (PAD100 系、C# 実装) の 2 系統。</summary>
public static class Linter
{
    private static readonly Regex TwoSegmentAction =
        new(@"^\s*(?<id>[A-Z][A-Za-z]+\.[A-Z][A-Za-z]+)\s+[A-Za-z]+:", RegexOptions.Compiled);

    private static readonly Regex UnresolvedParam = new(@"\{\{", RegexOptions.Compiled);

    private static readonly Regex BlockOpen =
        new(@"^\s*(?<kw>IF\b.*\bTHEN|LOOP\b)", RegexOptions.Compiled);

    public static IReadOnlyList<Finding> Lint(string padText, RuleSet rules)
    {
        var findings = new List<Finding>();
        var lines = padText.Replace("\r\n", "\n").Split('\n');
        var inString = StringMask(padText.Replace("\r\n", "\n"));

        // 各行について: コメント行を飛ばし、scope に応じてマスク済みテキストへ適用
        var offset = 0;
        for (var n = 0; n < lines.Length; n++)
        {
            var line = lines[n];
            var lineLen = line.Length;
            var trimmed = line.TrimStart();
            var isComment = trimmed.StartsWith('#');
            if (!isComment)
            {
                // code 用マスク: 文字列内の文字を空白化
                var codeChars = line.ToCharArray();
                var strChars = line.ToCharArray();
                for (var c = 0; c < lineLen; c++)
                {
                    var pos = offset + c;
                    var ins = pos < inString.Length && inString[pos];
                    if (ins) codeChars[c] = ' '; else strChars[c] = ' ';
                }
                var codeOnly = new string(codeChars);
                var stringOnly = new string(strChars);

                foreach (var rule in rules.Rules)
                {
                    var target = rule.ParsedScope switch
                    {
                        RuleScope.Code => codeOnly,
                        RuleScope.String => stringOnly,
                        _ => line,
                    };
                    if (Regex.IsMatch(target, rule.Pattern))
                        findings.Add(new Finding(rule.Id, n + 1, rule.ParsedSeverity,
                            rule.Message, rule.Fix));
                }

                // PAD002: 2 セグメント識別子 (許可リスト以外)
                var tm = TwoSegmentAction.Match(codeOnly);
                if (tm.Success && !rules.TwoSegmentAllow.Contains(tm.Groups["id"].Value))
                {
                    var id = tm.Groups["id"].Value;
                    findings.Add(new Finding("PAD002", n + 1, Severity.Error,
                        $"アクション識別子 '{id}' は 2 セグメントです。`モジュール.アクション.バリアント` の 3 セグメント形式が必要です",
                        "3 セグメント識別子へ修正 (例: Scripting.RunDOSCommand.RunDOSCommand)"));
                }
            }
            offset += lineLen + 1; // +1 = '\n'
        }

        findings.AddRange(StructuralLint(lines, n =>
        {
            var start = 0;
            for (var i = 0; i < n; i++) start += lines[i].Length + 1;
            var first = lines[n].Length - lines[n].TrimStart().Length;
            var pos = start + first;
            return pos < inString.Length && inString[pos];
        }));
        return findings.OrderBy(f => f.Line).ThenBy(f => f.RuleId).ToList();
    }

    /// <summary>`$'''...'''` の内側を true とする文字位置マスクを作る。</summary>
    private static bool[] StringMask(string text)
    {
        var mask = new bool[text.Length];
        var inside = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (!inside && i + 3 < text.Length
                && text[i] == '$' && text[i + 1] == '\'' && text[i + 2] == '\'' && text[i + 3] == '\'')
            {
                inside = true;
                i += 3; // `$'''` 自体は文字列部とみなす
                for (var j = i - 3; j <= i; j++) mask[j] = true;
                continue;
            }
            if (inside && i + 2 < text.Length
                && text[i] == '\'' && text[i + 1] == '\'' && text[i + 2] == '\'')
            {
                mask[i] = mask[i + 1] = mask[i + 2] = true;
                i += 2;
                inside = false;
                continue;
            }
            mask[i] = inside;
        }
        return mask;
    }

    /// <summary>構造ルール PAD100 (ブロック対応) / PAD101 (未解決 {{ }})。
    /// isInString(n) = 行の先頭非空白文字が文字列リテラル内なら true。</summary>
    private static IEnumerable<Finding> StructuralLint(string[] lines, Func<int, bool> isInString)
    {
        var stack = new Stack<(string Kind, int Line)>();
        for (var n = 0; n < lines.Length; n++)
        {
            var t = lines[n].TrimStart();
            if (t.StartsWith('#') || t.Length == 0) continue;

            if (UnresolvedParam.IsMatch(t))
                yield return new Finding("PAD101", n + 1, Severity.Error,
                    "未解決のテンプレートパラメータ '{{' が残っています", null);

            if (isInString(n)) continue; // 複数行 $'''...''' の中身はコードではない

            var open = BlockOpen.Match(t);
            if (open.Success)
            {
                var kw = open.Groups["kw"].Value;
                var kind = kw.StartsWith("IF", StringComparison.Ordinal) ? "IF" : "LOOP";
                stack.Push((kind, n + 1));
                continue;
            }
            if (t.StartsWith("ELSE", StringComparison.Ordinal))
            {
                if (stack.Count == 0 || stack.Peek().Kind != "IF")
                    yield return new Finding("PAD100", n + 1, Severity.Error,
                        "ELSE が IF ブロックの外にあります", null);
                continue;
            }
            if (t.StartsWith("END", StringComparison.Ordinal))
            {
                if (stack.Count == 0)
                    yield return new Finding("PAD100", n + 1, Severity.Error,
                        "対応する IF/LOOP のない END があります", null);
                else
                    stack.Pop();
            }
        }
        foreach (var (kind, line) in stack)
            yield return new Finding("PAD100", line, Severity.Error,
                $"{kind} ブロックが END で閉じられていません", null);
    }
}
