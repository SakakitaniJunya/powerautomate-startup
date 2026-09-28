using System.Text.Json;

namespace PadKit.Flow;

/// <summary>レシピの `#! requires:` が参照する環境値の集合 (JSON オブジェクト 1 枚)。</summary>
public sealed class Profile
{
    private readonly IReadOnlyDictionary<string, JsonElement> _values;

    public string Name { get; }

    public Profile(IReadOnlyDictionary<string, JsonElement> values, string name = "")
    {
        _values = values;
        Name = name;
    }

    public static Profile Load(string path) => Load(new[] { path });

    /// <summary>複数プロファイルを後勝ちで重ね合わせる
    /// (例: dev.json + dev.local.json → ローカルの秘密値だけ上書き)。</summary>
    public static Profile Load(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            throw new ArgumentException("--profile を 1 つ以上指定してください");
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var path in paths)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new RenderException(new[]
                {
                    new Diagnostic("E_PROFILE", 0, Severity.Error,
                        $"プロファイル '{path}' は JSON オブジェクトである必要があります"),
                });
            foreach (var p in doc.RootElement.EnumerateObject())
                dict[p.Name] = p.Value.Clone();
            names.Add(Path.GetFileNameWithoutExtension(path));
        }
        return new Profile(dict, string.Join("+", names));
    }

    public bool TryGet(string key, out JsonElement value) => _values.TryGetValue(key, out value);
}
