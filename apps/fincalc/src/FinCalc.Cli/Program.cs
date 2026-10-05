using System.Text.Json;
using FinCalc.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// PAD からキックされる CLI。stdout に 1 行 JSON を返し、エラーは stderr + exit 2。
// `fincalc mcp` で MCP stdio サーバーとして起動（MCP クライアントからツールとして呼べる）。
// サブコマンド本体は Commands/ 配下の ICommand 実装に分離し、ここでは引数パースと
// ディスパッチだけを行う。
if (args.Length > 0 && args[0].Equals("mcp", StringComparison.OrdinalIgnoreCase))
{
    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(NullLoggerProvider.Instance);
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
    await builder.Build().RunAsync();
    return 0;
}

var jsonOpts = new JsonSerializerOptions
{
    WriteIndented = false,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
};

var registry = CommandRegistry.CreateDefault();

try
{
    var argsList = args.ToList();
    if (argsList.Count == 0) throw new ArgumentException(Usage(registry));

    var cmd = string.Join(' ', argsList.Take(2).TakeWhile(a => !a.StartsWith("--")));
    var opts = ParseOpts(argsList.SkipWhile(a => !a.StartsWith("--")).ToList());

    var result = registry.Resolve(cmd).Run(new CommandArgs(opts));
    Console.WriteLine(JsonSerializer.Serialize(result, jsonOpts));
    return 0;
}
catch (ArgumentException ex) when (ex.Message.StartsWith("不明なコマンド"))
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { error = $"{ex.Message}\n{Usage(registry)}" }, jsonOpts));
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }, jsonOpts));
    return 2;
}

static Dictionary<string, string> ParseOpts(List<string> args)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Count; i++)
    {
        if (!args[i].StartsWith("--")) continue;
        var key = args[i][2..];
        if (i + 1 >= args.Count || args[i + 1].StartsWith("--"))
            throw new ArgumentException($"--{key} に値がありません");
        d[key] = args[++i];
    }
    return d;
}

static string Usage(CommandRegistry registry) => """
    fincalc — 減価償却・税計算 CLI（PAD 連携用。stdout に 1 行 JSON）

    """
    + string.Join("\n", registry.All.Select(c => c.Usage))
    + """

    共通: --columns map.json で部署別のヘッダ名対応を上書き（excel 系のみ）
    """;
