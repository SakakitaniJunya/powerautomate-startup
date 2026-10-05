using FinCalc;
using FinCalc.Excel;

namespace FinCalc.Cli.Commands;

/// <summary>
/// CLI サブコマンド1件の責務。「dep schedule」「excel dep-batch」等の
/// 名前空間つきコマンド名で <see cref="CommandRegistry"/> に登録する。
/// 新しい機能を足すときはこのインターフェースを実装して登録するだけでよい。
/// </summary>
public interface ICommand
{
    /// <summary>"dep schedule" のような2語コマンド名。</summary>
    string Name { get; }

    /// <summary>Usage 表示用の1行。</summary>
    string Usage { get; }

    /// <summary>実行して stdout JSON のもとになるオブジェクトを返す。</summary>
    object Run(CommandArgs args);
}

/// <summary>--key value 形式でパース済みのコマンド引数。</summary>
public sealed class CommandArgs
{
    private readonly IReadOnlyDictionary<string, string> _opts;

    public CommandArgs(IReadOnlyDictionary<string, string> opts) => _opts = opts;

    public string? Opt(string key) => _opts.TryGetValue(key, out var v) ? v : null;

    public string Req(string key) =>
        _opts.TryGetValue(key, out var v) ? v : throw new ArgumentException($"必須オプション --{key} がありません");

    public long ReqLong(string key) =>
        long.TryParse(Req(key), out var v) ? v : throw new ArgumentException($"--{key} は整数で指定: {_opts[key]}");

    public int ReqInt(string key) =>
        int.TryParse(Req(key), out var v) ? v : throw new ArgumentException($"--{key} は整数で指定: {_opts[key]}");

    public int? OptInt(string key) =>
        _opts.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : null;

    /// <summary>--rate 10|8|0.10 → 0.10|0.08</summary>
    public decimal ReqRate()
    {
        var s = Req("rate");
        if (!decimal.TryParse(s, out var v)) throw new ArgumentException($"--rate は数値で指定: {s}");
        return v > 1m ? v / 100m : v;
    }

    public RoundMode OptRound() =>
        _opts.TryGetValue("round", out var v)
            ? v.ToLowerInvariant() switch
            {
                "floor" => RoundMode.Floor,
                "ceiling" or "ceil" => RoundMode.Ceiling,
                "nearest" or "round" => RoundMode.Nearest,
                _ => throw new ArgumentException($"--round は floor|ceiling|nearest。指定値: {v}")
            }
            : RoundMode.Floor;

    /// <summary>--columns で部署別列対応 JSON を読む。未指定なら既定の対応表。</summary>
    public ColumnMapSet LoadColumns() =>
        _opts.TryGetValue("columns", out var path) ? ColumnMapSet.Load(path) : ColumnMapSet.Default;
}
