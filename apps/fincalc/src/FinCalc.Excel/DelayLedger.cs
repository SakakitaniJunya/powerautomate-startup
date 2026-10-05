using ClosedXML.Excel;

namespace FinCalc.Excel;

/// <summary>
/// 遅延理由回収台帳の読み書き。担当者×複数案件を1シートで管理し、
/// 遅延理由の入力(提出/一時保存)と日次の未提出チェックを支える。
///
/// 台帳列（論理名 → 既定エイリアスは <see cref="HeaderMap.DelayColumns"/>）:
///   person / email / item / due / reason / status
/// 状態の取りうる値: 未入力(空も同義) / 一時保存 / 提出済
/// </summary>
public sealed class DelayLedger
{
    public const string StatusPending = "未入力";
    public const string StatusDraft = "一時保存";
    public const string StatusSubmitted = "提出済";

    /// <summary>台帳1行。Row は Excel 上の行番号（書き戻しのキー）。</summary>
    public sealed record Item(
        int Row, string Sheet, string Person, string Email, string ItemName,
        string Due, string? Reason, string Status)
    {
        /// <summary>提出済以外は全て未提出扱い（一時保存も日次チェックの対象）。</summary>
        public bool IsPending => !IsSubmitted;
        public bool IsSubmitted => Status == StatusSubmitted;
    }

    /// <summary>担当者1人分の集計。</summary>
    public sealed record PersonStatus(
        string Person, string Email, int Total, int Submitted, int Draft,
        IReadOnlyList<Item> PendingItems)
    {
        public int Pending => PendingItems.Count;
    }

    public sealed record SubmitResult(
        string Person, string ItemName, int Row, string Status,
        int PersonRemaining, int TotalPending);

    private static readonly string[] Fields =
        { "person", "email", "item", "due", "reason", "status" };

    private readonly ColumnMap _columns;

    public DelayLedger(ColumnMap? columns = null) => _columns = columns ?? ColumnMap.Delay;

    /// <summary>全行を読み出す。person/item の無い行はスキップ。</summary>
    public IReadOnlyList<Item> ReadItems(string path, string? sheet = null, int? headerRow = null)
    {
        using var wb = new XLWorkbook(path);
        var items = new List<Item>();
        foreach (var ws in SheetReader.TargetSheets(wb, sheet))
        {
            SheetReader.SheetRows rows;
            try { rows = SheetReader.Read(ws, headerRow, _columns.AllHeaders); }
            catch (ArgumentException) { continue; }

            foreach (var row in rows.Rows)
            {
                var person = _columns.Get(row, "person");
                var itemName = _columns.Get(row, "item");
                if (string.IsNullOrWhiteSpace(person) && string.IsNullOrWhiteSpace(itemName))
                    continue;
                items.Add(new(
                    row.RowNumber, rows.SheetName,
                    person ?? "",
                    _columns.Get(row, "email") ?? "",
                    itemName ?? "",
                    _columns.Get(row, "due") ?? "",
                    _columns.Get(row, "reason"),
                    _columns.Get(row, "status") ?? StatusPending));
            }
        }
        return items;
    }

    /// <summary>担当者ごとの pending 一覧（カード送信用）。email は行から引く。</summary>
    public IReadOnlyList<PersonStatus> Status(string path, string? sheet = null, int? headerRow = null) =>
        ReadItems(path, sheet, headerRow)
            .GroupBy(i => i.Person)
            .Select(g => new PersonStatus(
                g.Key,
                g.Select(i => i.Email).FirstOrDefault(e => e != "") ?? "",
                g.Count(),
                g.Count(i => i.IsSubmitted),
                g.Count(i => i.Status == StatusDraft),
                g.Where(i => i.IsPending).ToList()))
            .OrderByDescending(p => p.Pending)
            .ToList();

    /// <summary>
    /// 遅延理由を書き戻す。--draft なら状態=一時保存、通常は提出済。
    /// 対象行は Excel 行番号で指定する（delay-list の items[].row）。
    /// </summary>
    public SubmitResult Submit(
        string path, int row, string reason, bool draft, string? sheet = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("遅延理由が空です");

        string person, itemName;
        var status = draft ? StatusDraft : StatusSubmitted;
        using (var wb = new XLWorkbook(path))
        {
            var ws = sheet is null ? wb.Worksheets.First() : SheetReader.TargetSheets(wb, sheet).First();
            var headerRow = DetectHeaderRow(ws);
            if (row <= headerRow)
                throw new ArgumentException($"行{row} は台帳データ行ではありません（ヘッダ行: {headerRow}）");
            var cols = MapColumns(ws, headerRow);

            if (!cols.TryGetValue("reason", out var reasonCol))
                throw new ArgumentException($"シート '{ws.Name}' に遅延理由列がありません");
            cols.TryGetValue("status", out var statusCol);

            person = Cell(ws, row, cols, "person");
            itemName = Cell(ws, row, cols, "item");
            if (string.IsNullOrWhiteSpace(person) && string.IsNullOrWhiteSpace(itemName))
                throw new ArgumentException($"行{row} は台帳データ行ではありません");

            ws.Cell(row, reasonCol).Value = reason;
            if (statusCol > 0) ws.Cell(row, statusCol).Value = status;

            wb.Save();
        }

        // 保存後に残数を集計（呼び出し側の進捗表示用）
        var items = ReadItems(path, sheet);
        var me = items.Where(i => i.Person == person).ToList();
        return new(person, itemName, row, status,
            me.Count(i => i.IsPending), items.Count(i => i.IsPending));
    }

    private static string Cell(IXLWorksheet ws, int row, Dictionary<string, int> cols, string logical) =>
        cols.TryGetValue(logical, out var c) ? ws.Cell(row, c).GetString().Trim() : "";

    /// <summary>ヘッダ行を特定（SheetReader と同じスコアリング）。</summary>
    private int DetectHeaderRow(IXLWorksheet ws)
    {
        var known = _columns.AllHeaders.Select(SheetReader.Normalize).ToHashSet();
        var last = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 1, 10);
        var best = 1; var bestScore = 0;
        for (var r = 1; r <= last; r++)
        {
            var score = ws.Row(r).CellsUsed()
                .Select(c => SheetReader.Normalize(c.GetString()))
                .Count(known.Contains);
            if (score > bestScore) { bestScore = score; best = r; }
        }
        if (bestScore == 0)
            throw new ArgumentException($"シート '{ws.Name}' に台帳ヘッダ行がありません");
        return best;
    }

    /// <summary>論理名 → 列番号の写像をヘッダ行から作る。</summary>
    private Dictionary<string, int> MapColumns(IXLWorksheet ws, int headerRow)
    {
        var map = new Dictionary<string, int>();
        foreach (var cell in ws.Row(headerRow).CellsUsed())
        {
            var h = SheetReader.Normalize(cell.GetString());
            foreach (var logical in Fields)
            {
                if (map.ContainsKey(logical)) continue;
                if (_columns.AliasesFor(logical).Select(SheetReader.Normalize).Contains(h))
                    map[logical] = cell.Address.ColumnNumber;
            }
        }
        return map;
    }

    /// <summary>サンプル台帳を生成（テスト・モック動作用）。</summary>
    public static string CreateSample(string outputPath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("遅延台帳");
        var headers = new[] { "担当者", "メール", "案件", "期限", "遅延理由", "状態" };
        for (var c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
        }
        var rows = new object?[,]
        {
            { "田中", "tanaka@example.com", "案件A-101", "2026-10-01", "", "未入力" },
            { "田中", "tanaka@example.com", "案件A-102", "2026-10-02", "", "未入力" },
            { "田中", "tanaka@example.com", "案件A-103", "2026-09-28", "部品入荷待ち", "提出済" },
            { "佐藤", "sato@example.com", "案件B-201", "2026-10-03", "", "未入力" },
            { "佐藤", "sato@example.com", "案件B-202", "2026-09-30", "確認中のため暫定", "一時保存" },
            { "鈴木", "suzuki@example.com", "案件C-301", "2026-10-04", "", "未入力" },
        };
        for (var r = 0; r < rows.GetLength(0); r++)
            for (var c = 0; c < rows.GetLength(1); c++)
                ws.Cell(2 + r, c + 1).Value = XLCellValue.FromObject(rows[r, c]);
        ws.Columns().AdjustToContents();
        wb.SaveAs(outputPath);
        return outputPath;
    }
}
