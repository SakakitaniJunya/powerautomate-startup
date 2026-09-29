using System.Text.Json;
using ClosedXML.Excel;
using FinCalc;
using FinCalc.Depreciation;
using FinCalc.Excel;
using FinCalc.Tax;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// PAD からキックされる CLI。stdout に 1 行 JSON を返し、エラーは stderr + exit 2。
// `fincalc mcp` で MCP stdio サーバーとして起動（MCP クライアントからツールとして呼べる）。
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

try
{
    var argsList = args.ToList();
    var result = Dispatch(argsList);
    Console.WriteLine(JsonSerializer.Serialize(result, jsonOpts));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { error = ex.Message }, jsonOpts));
    return 2;
}

static object Dispatch(List<string> args)
{
    if (args.Count == 0) throw new ArgumentException(Usage());

    var cmd = string.Join(' ', args.Take(2).TakeWhile(a => !a.StartsWith("--")));
    var opts = ParseOpts(args.SkipWhile(a => !a.StartsWith("--")).ToList());

    return cmd switch
    {
        "dep schedule" => DepSchedule(opts),
        "rates" => Rates(opts),
        "excel dep-batch" => ExcelDepBatch(opts),
        "excel invoice" => ExcelInvoice(opts),
        "excel read" => ExcelRead(opts),
        "excel init-sample" => ExcelInitSample(opts),
        "tax consumption" => TaxConsumption(opts),
        "tax consumption-net" => TaxConsumptionNet(opts),
        "tax invoice" => TaxInvoice(opts),
        "tax withholding" => TaxWithholding(opts),
        "tax corporate" => TaxCorporate(opts),
        _ => throw new ArgumentException($"不明なコマンド: '{cmd}'\n{Usage()}")
    };
}

static object DepSchedule(Dictionary<string, string> o)
{
    var method = Req(o, "method") switch
    {
        "straight" => DepreciationMethod.StraightLine,
        "declining" or "db200" => DepreciationMethod.DecliningBalance200,
        var m => throw new ArgumentException($"method は straight|declining。指定値: {m}")
    };
    var cost = ReqLong(o, "cost");
    var life = ReqInt(o, "life");
    var months = OptInt(o, "months") ?? 12;
    var round = OptRound(o);

    var rows = DepreciationCalculator.Schedule(cost, life, method, months, round);
    return new
    {
        cost,
        usefulLifeYears = life,
        method = method.ToString(),
        firstYearMonths = months,
        rounding = round.ToString().ToLowerInvariant(),
        rate = method == DepreciationMethod.StraightLine
            ? RateTable.StraightLineRate(life)
            : RateTable.DecliningBalance200(life).Rate,
        schedule = rows
    };
}

static object Rates(Dictionary<string, string> o)
{
    var life = ReqInt(o, "life");
    var db = RateTable.DecliningBalance200(life);
    return new
    {
        usefulLifeYears = life,
        straightLineRate = RateTable.StraightLineRate(life),
        decliningBalanceRate = db.Rate,
        revisedRate = db.RevisedRate,
        guaranteeRate = db.GuaranteeRate,
    };
}

static object TaxConsumption(Dictionary<string, string> o)
{
    var r = ConsumptionTax.AddTax(ReqLong(o, "net"), ReqRate(o), OptRound(o));
    return new { r.Net, rate = r.Rate, r.Tax, r.Gross };
}

static object TaxConsumptionNet(Dictionary<string, string> o)
{
    var r = ConsumptionTax.ExtractTax(ReqLong(o, "gross"), ReqRate(o), OptRound(o));
    return new { r.Gross, rate = r.Rate, r.Net, r.Tax };
}

static object TaxInvoice(Dictionary<string, string> o)
{
    var json = Req(o, "lines").Replace(",]", "]");
    var lines = JsonSerializer.Deserialize<List<InvoiceLine>>(json, JsonOptsHolder.DeserializeOpts)
        ?? throw new ArgumentException("--lines の JSON が空です");
    var r = ConsumptionTax.Invoice(
        lines.Select(l => (l.Net, l.Rate > 1m ? l.Rate / 100m : l.Rate)),
        OptRound(o));
    return new { r.Groups, r.NetTotal, r.TaxTotal, r.GrossTotal };
}

static object ExcelDepBatch(Dictionary<string, string> o)
{
    var input = Req(o, "input");
    var r = DepBatch.Run(input, Opt(o, "sheet"), OptInt(o, "header-row"), Opt(o, "output"));
    return new { input, output = r.OutputPath, r.OkCount, r.ErrorCount, summary = r.Summary };
}

static object ExcelInvoice(Dictionary<string, string> o)
{
    var input = Req(o, "input");
    var output = Opt(o, "output") ?? Path.ChangeExtension(input, ".tax-result.xlsx");
    var r = InvoiceBatch.Run(input, Opt(o, "sheet"), OptInt(o, "header-row"), output, OptRound(o));
    return new { input, output, r.Groups, r.NetTotal, r.TaxTotal, r.GrossTotal };
}

static object ExcelRead(Dictionary<string, string> o)
{
    var input = Req(o, "input");
    using var wb = new XLWorkbook(input);
    var sheets = SheetReader.TargetSheets(wb, Opt(o, "sheet"))
        .Select(ws =>
        {
            var rows = SheetReader.Read(ws, OptInt(o, "header-row"),
                HeaderMap.AssetColumns.Values.Concat(HeaderMap.InvoiceColumns.Values).SelectMany(a => a));
            return new
            {
                sheet = rows.SheetName,
                rows = rows.Rows.Select(x => new { row = x.RowNumber, cells = x.Cells }).ToList()
            };
        })
        .ToList();
    return new { input, sheets };
}

static object ExcelInitSample(Dictionary<string, string> o)
{
    var output = Opt(o, "output") ?? Path.Combine(Directory.GetCurrentDirectory(), "assets.xlsx");
    return new { output = SampleWorkbook.CreateAssetLedger(output) };
}

static string? Opt(Dictionary<string, string> o, string key) =>
    o.TryGetValue(key, out var v) ? v : null;

static object TaxWithholding(Dictionary<string, string> o)
{
    var r = WithholdingTax.ForFee(ReqLong(o, "amount"));
    return new { r.GrossAmount, r.Tax, r.NetPayment, r.EffectiveRate };
}

static object TaxCorporate(Dictionary<string, string> o)
{
    var r = CorporateTax.Estimate(ReqLong(o, "income"));
    return new { r.TaxableIncome, r.NationalTax, r.LocalCorporateTax, r.Total };
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

static string Req(Dictionary<string, string> o, string key) =>
    o.TryGetValue(key, out var v) ? v : throw new ArgumentException($"必須オプション --{key} がありません");

static long ReqLong(Dictionary<string, string> o, string key) =>
    long.TryParse(Req(o, key), out var v) ? v : throw new ArgumentException($"--{key} は整数で指定: {o[key]}");

static int ReqInt(Dictionary<string, string> o, string key) =>
    int.TryParse(Req(o, key), out var v) ? v : throw new ArgumentException($"--{key} は整数で指定: {o[key]}");

static int? OptInt(Dictionary<string, string> o, string key) =>
    o.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : null;

static decimal ReqRate(Dictionary<string, string> o)
{
    var s = Req(o, "rate");
    if (!decimal.TryParse(s, out var v)) throw new ArgumentException($"--rate は数値で指定: {s}");
    return v > 1m ? v / 100m : v; // 10 → 0.10
}

static RoundMode OptRound(Dictionary<string, string> o) =>
    o.TryGetValue("round", out var v)
        ? v.ToLowerInvariant() switch
        {
            "floor" => RoundMode.Floor,
            "ceiling" or "ceil" => RoundMode.Ceiling,
            "nearest" or "round" => RoundMode.Nearest,
            _ => throw new ArgumentException($"--round は floor|ceiling|nearest。指定値: {v}")
        }
        : RoundMode.Floor;

static string Usage() => """
    fincalc — 減価償却・税計算 CLI（PAD 連携用。stdout に 1 行 JSON）

    dep schedule          --method straight|declining --cost N --life Y [--months M] [--round floor|ceiling|nearest]
    rates                 --life Y
    excel dep-batch       --input assets.xlsx [--sheet 名|番号|*] [--header-row N] [--output out.xlsx]
    excel invoice         --input lines.xlsx [--sheet 名|番号|*] [--header-row N] [--output out.xlsx] [--round ...]
    excel read            --input x.xlsx [--sheet 名|番号|*] [--header-row N]
    excel init-sample     [--output assets.xlsx]
    tax consumption       --net N --rate 10|8 [--round ...]
    tax consumption-net   --gross N --rate 10|8 [--round ...]
    tax invoice           --lines '[{"net":1000,"rate":10}, ...]' [--round ...]
    tax withholding       --amount N
    tax corporate         --income N
    """;

file sealed record InvoiceLine(long Net, decimal Rate);

file static class JsonOptsHolder
{
    public static readonly JsonSerializerOptions DeserializeOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
