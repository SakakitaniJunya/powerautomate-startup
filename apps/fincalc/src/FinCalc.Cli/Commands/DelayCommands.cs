using FinCalc.Excel;

namespace FinCalc.Cli.Commands;

/// <summary>excel delay-list — 遅延台帳を読み、担当者ごとの未提出案件一覧を返す。</summary>
public sealed class ExcelDelayListCommand : ICommand
{
    public string Name => "excel delay-list";
    public string Usage => "excel delay-list      --input delay.xlsx [--sheet 名|番号|*] [--header-row N] [--columns map.json | --dept profile.json]";

    public object Run(CommandArgs a)
    {
        var input = a.Req("input");
        var items = new DelayLedger(DelayColumns(a)).ReadItems(
            input, a.Opt("sheet"), a.OptInt("header-row"));

        var persons = items
            .GroupBy(i => i.Person)
            .Select(g => new
            {
                person = g.Key,
                email = g.Select(i => i.Email).FirstOrDefault(e => e != "") ?? "",
                total = g.Count(),
                submitted = g.Count(i => i.IsSubmitted),
                draft = g.Count(i => i.Status == DelayLedger.StatusDraft),
                pending = g.Count(i => i.IsPending),
                // カードに出すのは未提出案件だけ
                items = g.Where(i => i.IsPending).Select(i => new
                {
                    i.Row, i.Sheet, item = i.ItemName, i.Due,
                    reason = i.Reason, i.Status,
                }).ToList(),
            })
            .Where(p => p.pending > 0)
            .OrderByDescending(p => p.pending)
            .ToList();

        return new
        {
            input,
            totalItems = items.Count,
            pendingItems = items.Count(i => i.IsPending),
            persons,
        };
    }

    internal static ColumnMap DelayColumns(CommandArgs a) =>
        a.LoadDepartment()?.Columns.Delay ?? a.LoadColumns().Delay;
}

/// <summary>excel delay-submit — 1案件の遅延理由を台帳へ書き戻す。--draft で一時保存。</summary>
public sealed class ExcelDelaySubmitCommand : ICommand
{
    public string Name => "excel delay-submit";
    public string Usage => "excel delay-submit    --input delay.xlsx --row N --reason テキスト [--draft] [--sheet 名|番号|*] [--columns map.json | --dept profile.json]";

    public object Run(CommandArgs a)
    {
        var input = a.Req("input");
        var row = a.ReqInt("row");
        var reason = a.Req("reason");
        var draft = a.Opt("draft") is not null;

        var r = new DelayLedger(ExcelDelayListCommand.DelayColumns(a))
            .Submit(input, row, reason, draft, a.Opt("sheet"));

        return new
        {
            input,
            r.Person,
            item = r.ItemName,
            r.Row,
            r.Status,
            r.PersonRemaining,
            totalPending = r.TotalPending,
        };
    }
}

/// <summary>excel delay-status — 日次チェック用。担当者ごとの提出状況と未提出案件を返す。</summary>
public sealed class ExcelDelayStatusCommand : ICommand
{
    public string Name => "excel delay-status";
    public string Usage => "excel delay-status    --input delay.xlsx [--sheet 名|番号|*] [--header-row N] [--columns map.json | --dept profile.json]";

    public object Run(CommandArgs a)
    {
        var input = a.Req("input");
        var persons = new DelayLedger(ExcelDelayListCommand.DelayColumns(a))
            .Status(input, a.Opt("sheet"), a.OptInt("header-row"));

        return new
        {
            input,
            allSubmitted = persons.All(p => p.Pending == 0),
            pendingPersons = persons.Count(p => p.Pending > 0),
            pendingItems = persons.Sum(p => p.Pending),
            persons = persons.Select(p => new
            {
                p.Person,
                p.Email,
                p.Total,
                p.Submitted,
                p.Draft,
                p.Pending,
                pendingItems = p.PendingItems.Select(i => new { i.Row, item = i.ItemName, i.Due }).ToList(),
            }).ToList(),
        };
    }
}

/// <summary>excel init-delay — 遅延台帳サンプル xlsx を生成。</summary>
public sealed class ExcelInitDelayCommand : ICommand
{
    public string Name => "excel init-delay";
    public string Usage => "excel init-delay      [--output delay.xlsx]";

    public object Run(CommandArgs a)
    {
        var output = a.Opt("output") ?? Path.Combine(Directory.GetCurrentDirectory(), "delay.xlsx");
        return new { output = DelayLedger.CreateSample(output) };
    }
}
