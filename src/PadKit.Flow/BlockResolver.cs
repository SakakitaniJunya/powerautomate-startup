using System.Text.RegularExpressions;

namespace PadKit.Flow;

/// <summary>ブロック定義: `#! param` ディレクティブで宣言した引数と本文行。</summary>
public sealed class Block
{
    public sealed record Param(string Name, string? Default)
    {
        public bool Required => Default is null;
    }

    public required string Name { get; init; }
    public required IReadOnlyList<Param> Params { get; init; }
    public required IReadOnlyList<string> Body { get; init; }
    public required string SourcePath { get; init; }
}

/// <summary>`<name>.pad` ブロックを探索ディレクトリ群から解決する。</summary>
public sealed class BlockResolver
{
    private static readonly Regex ParamDirective =
        new(@"^#!\s*param\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s+default=""(?<d>(?:[^""\\]|\\.)*)"")?\s*$",
            RegexOptions.Compiled);

    private readonly List<string> _dirs;

    public BlockResolver(IEnumerable<string> dirs)
    {
        _dirs = dirs.ToList();
    }

    public IReadOnlyList<string> Dirs => _dirs;

    /// <summary>既定の探索順: レシピの blocks/ → CLI --blocks → padkit 同梱 blocks/。</summary>
    public static BlockResolver Create(string? recipeDir, IEnumerable<string> cliDirs)
    {
        var dirs = new List<string>();
        if (recipeDir is not null)
            dirs.Add(Path.Combine(recipeDir, "blocks"));
        dirs.AddRange(cliDirs);
        var bundled = BundledBlocksDir();
        if (bundled is not null)
            dirs.Add(bundled);
        return new BlockResolver(dirs);
    }

    /// <summary>同梱 blocks/ の場所: PADKIT_BLOCKS 環境変数、未設定なら
    /// BaseDirectory から上へ `blocks/` と `PadKit.sln` を両方持つ親を探す。</summary>
    public static string? BundledBlocksDir()
    {
        var env = Environment.GetEnvironmentVariable("PADKIT_BLOCKS");
        if (!string.IsNullOrEmpty(env))
            return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "blocks");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "PadKit.sln")))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public Block? Resolve(string name)
    {
        foreach (var dir in _dirs)
        {
            var path = Path.Combine(dir, name + ".pad");
            if (File.Exists(path))
                return Parse(name, path);
        }
        return null;
    }

    /// <summary>解決可能なブロックを全列挙 (先勝ちの探索順で同名は最初のもの)。</summary>
    public IReadOnlyList<Block> ListAll()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<Block>();
        foreach (var dir in _dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.pad").OrderBy(f => f))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (seen.Add(name))
                    list.Add(Parse(name, file));
            }
        }
        return list;
    }

    private static Block Parse(string name, string path)
    {
        var pars = new List<Block.Param>();
        var body = new List<string>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var m = ParamDirective.Match(raw);
            if (m.Success)
            {
                var g = m.Groups["d"];
                pars.Add(new Block.Param(
                    m.Groups[1].Value,
                    g.Success ? g.Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : null));
                continue;
            }
            // `#!` で始まる行はディレクティブとして本文から落とす
            if (raw.StartsWith("#!", StringComparison.Ordinal)) continue;
            body.Add(raw);
        }
        return new Block { Name = name, Params = pars, Body = body, SourcePath = path };
    }
}
