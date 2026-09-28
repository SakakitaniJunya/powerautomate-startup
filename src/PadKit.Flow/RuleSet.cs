using System.Text.Json;
using System.Text.Json.Serialization;

namespace PadKit.Flow;

public enum RuleScope
{
    /// <summary>行全体にマッチ (コメント行は除く)。</summary>
    Line,
    /// <summary>`$'''...'''` 文字列リテラルの外側だけにマッチ。</summary>
    Code,
    /// <summary>`$'''...'''` 文字列リテラルの内側だけにマッチ。</summary>
    String,
}

public sealed class LintRule
{
    public required string Id { get; init; }
    public required string Severity { get; init; }
    public required string Pattern { get; init; }
    public required string Message { get; init; }
    public string? Fix { get; init; }
    public string Scope { get; init; } = "line";

    public Severity ParsedSeverity =>
        Severity.Equals("error", StringComparison.OrdinalIgnoreCase) ? Flow.Severity.Error : Flow.Severity.Warning;

    public RuleScope ParsedScope => Scope.ToLowerInvariant() switch
    {
        "code" => RuleScope.Code,
        "string" => RuleScope.String,
        _ => RuleScope.Line,
    };
}

/// <summary>`rules/*.json` で与える lint ルールセット。正規表現ルールに加えて
/// 2 セグメント識別子の許可リスト (twoSegmentAllow) を持つ。</summary>
public sealed class RuleSet
{
    public required string PadVersion { get; init; }
    public required List<LintRule> Rules { get; init; }
    public List<string> TwoSegmentAllow { get; init; } = new();

    public static RuleSet Load(string path)
    {
        var rules = JsonSerializer.Deserialize<RuleSet>(File.ReadAllText(path),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
        if (rules is null)
            throw new InvalidDataException($"ルールファイル '{path}' を読み取れません");
        return rules;
    }
}
