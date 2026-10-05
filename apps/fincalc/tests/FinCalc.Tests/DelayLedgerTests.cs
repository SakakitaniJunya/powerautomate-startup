using ClosedXML.Excel;
using FinCalc.Excel;
using Xunit;

namespace FinCalc.Tests;

public class DelayLedgerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fincalc-test-{Guid.NewGuid():N}");

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private string Sample()
    {
        Directory.CreateDirectory(_dir);
        return DelayLedger.CreateSample(Path.Combine(_dir, "delay.xlsx"));
    }

    [Fact]
    public void ReadItems_ReadsAllRowsWithDefaults()
    {
        var items = new DelayLedger().ReadItems(Sample());

        Assert.Equal(6, items.Count);
        Assert.Equal("田中", items[0].Person);
        Assert.Equal("tanaka@example.com", items[0].Email);
        Assert.Equal("案件A-101", items[0].ItemName);
        Assert.True(items[0].IsPending);
        Assert.True(items[2].IsSubmitted);
        Assert.Equal("一時保存", items[4].Status);
    }

    [Fact]
    public void Status_GroupsPerPersonWithPendingItems()
    {
        var persons = new DelayLedger().Status(Sample());

        Assert.Equal(3, persons.Count);
        var tanaka = persons.First(p => p.Person == "田中");
        Assert.Equal(3, tanaka.Total);
        Assert.Equal(1, tanaka.Submitted);
        Assert.Equal(2, tanaka.Pending);
        Assert.Equal(2, tanaka.PendingItems.Count);
        Assert.Equal("tanaka@example.com", tanaka.Email);
    }

    [Fact]
    public void Submit_WritesReasonAndSubmittedStatus()
    {
        var path = Sample();
        var r = new DelayLedger().Submit(path, 2, "仕様確定待ち", draft: false);

        Assert.Equal("田中", r.Person);
        Assert.Equal("案件A-101", r.ItemName);
        Assert.Equal(DelayLedger.StatusSubmitted, r.Status);
        Assert.Equal(1, r.PersonRemaining); // A-102 が残る
        Assert.Equal(4, r.TotalPending);    // 田中A-102 + 佐藤B-201/B-202(一時保存) + 鈴木C-301

        var items = new DelayLedger().ReadItems(path);
        var row = items.Single(i => i.Row == 2);
        Assert.Equal("仕様確定待ち", row.Reason);
        Assert.Equal(DelayLedger.StatusSubmitted, row.Status);
    }

    [Fact]
    public void Submit_Draft_WritesDraftStatus()
    {
        var path = Sample();
        var r = new DelayLedger().Submit(path, 5, "確認中のため暫定", draft: true);

        Assert.Equal(DelayLedger.StatusDraft, r.Status);
        // draft は pending 扱い（未提出）: 佐藤は B-201(今回draft) + B-202(既存draft) で残2
        Assert.Equal(2, r.PersonRemaining);

        var row = new DelayLedger().ReadItems(path).Single(i => i.Row == 5);
        Assert.Equal("一時保存", row.Status);
        Assert.True(row.IsPending);
    }

    [Fact]
    public void Submit_EmptyReason_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new DelayLedger().Submit(Sample(), 2, "  ", draft: false));
    }

    [Fact]
    public void Submit_NonDataRow_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new DelayLedger().Submit(Sample(), 1, "x", draft: false)); // 1行目はヘッダ
    }

    [Fact]
    public void CustomColumnMap_ReadsDepartmentHeaders()
    {
        Directory.CreateDirectory(_dir);
        var input = Path.Combine(_dir, "dept-delay.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("台帳");
            // 部署独自ヘッダ
            ws.Cell(1, 1).Value = "オーナー名";
            ws.Cell(1, 2).Value = "タスク名";
            ws.Cell(1, 3).Value = "ステータス";
            ws.Cell(2, 1).Value = "高橋";
            ws.Cell(2, 2).Value = "問合せ対応";
            ws.Cell(2, 3).Value = "未入力";
            wb.SaveAs(input);
        }

        var map = ColumnMap.Delay.WithOverrides(new Dictionary<string, IReadOnlyList<string>>
        {
            ["person"] = ["オーナー名"],
            ["item"] = ["タスク名"],
            ["status"] = ["ステータス"],
        });
        var items = new DelayLedger(map).ReadItems(input);

        Assert.Single(items);
        Assert.Equal("高橋", items[0].Person);
        Assert.Equal("問合せ対応", items[0].ItemName);
    }
}
