using FinCalc;
using FinCalc.Tax;
using Xunit;

namespace FinCalc.Tests;

public class TaxTests
{
    [Fact]
    public void ConsumptionTax_AddTax_10Percent()
    {
        var r = new ConsumptionTaxCalculator().AddTax(1_000, ConsumptionTaxCalculator.StandardRate);
        Assert.Equal(100, r.Tax);
        Assert.Equal(1_100, r.Gross);
    }

    [Fact]
    public void ConsumptionTax_AddTax_ReducedRate_Floor()
    {
        var r = new ConsumptionTaxCalculator().AddTax(123, ConsumptionTaxCalculator.ReducedRate, RoundMode.Floor);
        Assert.Equal(9, r.Tax); // 123 × 0.08 = 9.84 → 切捨
        Assert.Equal(132, r.Gross);
    }

    [Fact]
    public void ConsumptionTax_ExtractTax()
    {
        var r = new ConsumptionTaxCalculator().ExtractTax(1_100, ConsumptionTaxCalculator.StandardRate);
        Assert.Equal(1_000, r.Net);
        Assert.Equal(100, r.Tax);
    }

    [Fact]
    public void ConsumptionTax_Invoice_RoundsOncePerRate()
    {
        // 10%対象3行(各333)と8%対象2行(各111)。税率ごとに合計してから端数処理。
        var r = new ConsumptionTaxCalculator().Invoice(new[]
        {
            (333L, 0.10m), (333L, 0.10m), (333L, 0.10m),
            (111L, 0.08m), (111L, 0.08m),
        });

        var g10 = Assert.Single(r.Groups, g => g.Rate == 0.10m);
        var g8 = Assert.Single(r.Groups, g => g.Rate == 0.08m);
        Assert.Equal(999, g10.NetTotal);
        Assert.Equal(99, g10.TaxTotal);   // 999 × 0.10 = 99.9 → 99
        Assert.Equal(222, g8.NetTotal);
        Assert.Equal(17, g8.TaxTotal);    // 222 × 0.08 = 17.76 → 17
        Assert.Equal(116, r.TaxTotal);
    }

    [Theory]
    [InlineData(100_000, 10_210)]
    [InlineData(1_000_000, 102_100)]
    [InlineData(1_500_000, 204_200)] // 102,100 + 500,000×0.2042 = 102,100+102,100
    public void WithholdingTax_Fee(long gross, long expectedTax)
    {
        var r = new WithholdingTaxCalculator().ForFee(gross);
        Assert.Equal(expectedTax, r.Tax);
        Assert.Equal(gross - expectedTax, r.NetPayment);
    }

    [Theory]
    [InlineData(5_000_000, 750_000)]              // 全部15%
    [InlineData(10_000_000, 1_664_000)]           // 800万×15% + 200万×23.2% = 1,200,000+464,000
    [InlineData(-1_000, 0)]
    public void CorporateTax_Estimate(long income, long expectedNational)
    {
        var r = new CorporateTaxCalculator().Estimate(income);
        Assert.Equal(expectedNational, r.NationalTax);
        Assert.Equal(r.NationalTax + r.LocalCorporateTax, r.Total);
        Assert.Equal((long)Math.Floor(expectedNational * 0.103m), r.LocalCorporateTax);
    }
}
