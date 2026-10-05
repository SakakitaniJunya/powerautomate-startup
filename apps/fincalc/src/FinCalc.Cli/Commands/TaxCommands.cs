using System.Text.Json;
using FinCalc.Tax;

namespace FinCalc.Cli.Commands;

/// <summary>tax consumption — 税抜→税込。</summary>
public sealed class TaxConsumptionCommand : ICommand
{
    private readonly ConsumptionTaxCalculator _tax = new();
    public string Name => "tax consumption";
    public string Usage => "tax consumption       --net N --rate 10|8 [--round ...]";

    public object Run(CommandArgs a)
    {
        var r = _tax.AddTax(a.ReqLong("net"), a.ReqRate(), a.OptRound());
        return new { r.Net, rate = r.Rate, r.Tax, r.Gross };
    }
}

/// <summary>tax consumption-net — 税込→税抜。</summary>
public sealed class TaxConsumptionNetCommand : ICommand
{
    private readonly ConsumptionTaxCalculator _tax = new();
    public string Name => "tax consumption-net";
    public string Usage => "tax consumption-net   --gross N --rate 10|8 [--round ...]";

    public object Run(CommandArgs a)
    {
        var r = _tax.ExtractTax(a.ReqLong("gross"), a.ReqRate(), a.OptRound());
        return new { r.Gross, rate = r.Rate, r.Net, r.Tax };
    }
}

/// <summary>tax invoice — インボイス方式の税率別集計。</summary>
public sealed class TaxInvoiceCommand : ICommand
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ConsumptionTaxCalculator _tax = new();

    public string Name => "tax invoice";
    public string Usage => "tax invoice           --lines '[{\"net\":1000,\"rate\":10}, ...]' [--round ...]";

    public object Run(CommandArgs a)
    {
        var json = a.Req("lines").Replace(",]", "]");
        var lines = JsonSerializer.Deserialize<List<InvoiceLine>>(json, JsonOpts)
            ?? throw new ArgumentException("--lines の JSON が空です");
        var r = _tax.Invoice(
            lines.Select(l => (l.Net, l.Rate > 1m ? l.Rate / 100m : l.Rate)),
            a.OptRound());
        return new { r.Groups, r.NetTotal, r.TaxTotal, r.GrossTotal };
    }

    private sealed record InvoiceLine(long Net, decimal Rate);
}

/// <summary>tax withholding — 報酬の源泉所得税。</summary>
public sealed class TaxWithholdingCommand : ICommand
{
    private readonly WithholdingTaxCalculator _tax = new();
    public string Name => "tax withholding";
    public string Usage => "tax withholding       --amount N";

    public object Run(CommandArgs a)
    {
        var r = _tax.ForFee(a.ReqLong("amount"));
        return new { r.GrossAmount, r.Tax, r.NetPayment, r.EffectiveRate };
    }
}

/// <summary>tax corporate — 法人税概算。</summary>
public sealed class TaxCorporateCommand : ICommand
{
    private readonly CorporateTaxCalculator _tax = new();
    public string Name => "tax corporate";
    public string Usage => "tax corporate         --income N";

    public object Run(CommandArgs a)
    {
        var r = _tax.Estimate(a.ReqLong("income"));
        return new { r.TaxableIncome, r.NationalTax, r.LocalCorporateTax, r.Total };
    }
}
