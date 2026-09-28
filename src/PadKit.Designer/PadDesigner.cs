using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace PadKit.Designer;

public sealed class PadDesignerException : Exception
{
    public PadDesignerException(string message) : base(message) { }
}

public sealed record CheckResult(int Actions, int Errors, IReadOnlyList<string> ErrorTexts);

public sealed record RunResult(bool Started, bool Finished, string LastStatus, TimeSpan Elapsed);

/// <summary>実行中の PAD デザイナー (PAD.Designer.exe) へ UIA3 で接続し、
/// アクション数取得・クリア・貼り付け・チェック・保存・実行を行う。
/// 座標は一切使わず AutomationId で要素を特定する (PAD 11.2609.183.0 で確認)。</summary>
public sealed class PadDesigner : IDisposable
{
    // AutomationId 一覧 (PAD 11.2609.183.0 の dump で確認済み。詳細は README)
    private const string CanvasId = "ProgramItemsListBoxActions";
    private const string StatusBarId = "ProgramDetailsStatusBarItem";
    private const string ErrorCountId = "ErrorCountTextBlock";
    private const string StatusPrefix = "Flow_status_";
    private const string SaveButtonId = "SaveDraftFlowButton";
    private const string RunButtonId = "StartFlowButton";
    private const string SummaryTextId = "ProgramItemTemplateSummaryTextBlock";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly Regex ErrorTextPattern =
        new("見つかりません|存在しません|不正|引数", RegexOptions.Compiled);

    private readonly UIA3Automation _automation;
    private readonly Window _window;

    public string WindowTitle => _window.Name;

    private PadDesigner(UIA3Automation automation, Window window)
    {
        _automation = automation;
        _window = window;
    }

    /// <summary>実行中の PAD デザイナーウィンドウへ接続する。
    /// 複数ある場合はフォアグラウンドのもの、無ければ先頭を選ぶ。</summary>
    public static PadDesigner Attach()
    {
        var automation = new UIA3Automation();
        var windows = DesignerWindows(automation).ToList();
        if (windows.Count == 0)
        {
            automation.Dispose();
            throw new PadDesignerException(
                "PAD デザイナー (PAD.Designer.exe) のウィンドウが見つかりません。先にデザイナーを開いてください");
        }

        Window chosen = windows[0];
        var fg = GetForegroundWindow();
        foreach (var w in windows)
        {
            if (fg != IntPtr.Zero && w.Properties.NativeWindowHandle.Value == fg)
            {
                chosen = w;
                break;
            }
        }
        return new PadDesigner(automation, chosen);
    }

    /// <summary>PAD 系トップレベルウィンドウの一覧 (診断用)。</summary>
    public static IEnumerable<string> ListWindows()
    {
        using var automation = new UIA3Automation();
        foreach (var w in automation.GetDesktop().FindAllChildren())
        {
            var pid = SafeInt(() => w.Properties.ProcessId.Value);
            var pname = pid > 0 ? SafeProcName(pid) : "";
            if (pname.StartsWith("PAD.", StringComparison.OrdinalIgnoreCase)
                || pname.Contains("Automate", StringComparison.OrdinalIgnoreCase))
                yield return $"[{w.ControlType}] '{w.Name}' pid={pid} {pname}";
        }
    }

    /// <summary>アクション数。ProgramDetailsStatusBarItem 内の "N アクション" 表示を読む。</summary>
    public int ActionCount()
    {
        var bar = FindById(StatusBarId);
        if (bar is null)
            throw new PadDesignerException($"ステータスバー '{StatusBarId}' が見つかりません");
        // "N 選択されたアクション" "N アクション" "N サブフロー" の順に並ぶ。2 番目がアクション数。
        var nums = bar.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Select(e => e.Name)
            .Select(n => Regex.Match(n, @"^(\d+)\s"))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();
        if (nums.Count >= 2) return nums[1];
        if (nums.Count == 1) return nums[0];
        return 0;
    }

    /// <summary>エラー数 (ErrorCountTextBlock)。見つからなければ 0。</summary>
    public int ErrorCount()
    {
        var el = FindById(ErrorCountId);
        if (el is null) return 0;
        var m = Regex.Match(el.Name, @"\d+");
        return m.Success ? int.Parse(m.Value) : 0;
    }

    /// <summary>フロー状態 ("ready" / "running" / "parsing" 等。Flow_status_ を剥がす)。
    /// 保存/実行中に UIA が一時応答しないことがあるため、失敗時は "unknown" を返す。</summary>
    public string Status()
    {
        try
        {
            var el = _window.FindAllDescendants()
                .FirstOrDefault(e =>
                {
                    var id = SafeStr(() => e.Properties.AutomationId.Value);
                    return id.StartsWith(StatusPrefix, StringComparison.Ordinal);
                });
            if (el is null) return "unknown";
            return el.Properties.AutomationId.Value[StatusPrefix.Length..];
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return "unknown";
        }
    }

    /// <summary>アクションキャンバスをフォーカスする。
    /// アクションがあれば先頭 ListItem を、空ならキャンバス中心をクリックする
    /// (空白部のクリックではキー入力が届かないことがある)。</summary>
    public void FocusCanvas()
    {
        _window.SetForeground();
        Thread.Sleep(300);
        var canvas = FindById(CanvasId)
            ?? throw new PadDesignerException(
                $"アクションキャンバス '{CanvasId}' が見つかりません");
        // キャンバス内で視覚的に上から 80px の点をクリックする。
        // - ListItem の BoundingRectangle は仮想化でキャンバス外を指すことがあり使わない
        // - 空キャンバス中央は EmptyState オーバーレイでフォーカスが取れない
        var r = canvas.BoundingRectangle;
        if (r.IsEmpty)
            throw new PadDesignerException("アクションキャンバスの位置を取得できません");
        Mouse.Click(new System.Drawing.Point(
            (int)(r.X + r.Width / 2), (int)(r.Y + Math.Min(80, r.Height / 4))));
        Thread.Sleep(400);
    }

    /// <summary>全アクションを削除する (最大 5 回リトライ)。</summary>
    public void ClearAll()
    {
        for (var i = 0; i < 5; i++)
        {
            if (ActionCount() == 0) return;
            FocusCanvas();
            SendKeys(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            Thread.Sleep(500);
            SendKeys(VirtualKeyShort.DELETE);
            Thread.Sleep(1200);
        }
        if (ActionCount() > 0)
            throw new PadDesignerException(
                $"アクションの削除に失敗しました (残り {ActionCount()} 件)");
    }

    /// <summary>テキストをクリップボード経由でキャンバスへ貼り付け、
    /// アクション数が 1.5 秒変わらなくなるまで待つ (最大 15 秒)。</summary>
    public void Paste(string text)
    {
        SetClipboard(text);
        FocusCanvas();
        SendKeys(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var last = -1;
        var stableSince = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            var n = ActionCount();
            if (n != last)
            {
                last = n;
                stableSince = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - stableSince > TimeSpan.FromMilliseconds(1500))
            {
                return;
            }
            Thread.Sleep(200);
        }
    }

    /// <summary>クリア → 貼り付け → アクション数/エラー数/エラーテキストを返す。</summary>
    public CheckResult Check(string text)
    {
        ClearAll();
        Paste(text);
        var errors = ErrorCount();
        var texts = errors > 0 ? CollectErrorTexts().Take(12).ToList()
            : (IReadOnlyList<string>)Array.Empty<string>();
        return new CheckResult(ActionCount(), errors, texts);
    }

    /// <summary>ドラフトを保存し、ステータスが ready に戻るまで待つ (30 秒)。</summary>
    public void Save(TimeSpan? timeout = null)
    {
        var btn = FindById(SaveButtonId)
            ?? throw new PadDesignerException($"保存ボタン '{SaveButtonId}' が見つかりません");
        btn.Click();
        WaitStatus("ready", timeout ?? DefaultTimeout);
    }

    /// <summary>実行ボタンを押し、running/parsing を経て ready に戻るまで待つ。</summary>
    public RunResult Run(TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(120);
        var btn = FindById(RunButtonId)
            ?? throw new PadDesignerException($"実行ボタン '{RunButtonId}' が見つかりません");
        if (btn.Patterns.Invoke.IsSupported)
            btn.Patterns.Invoke.Pattern.Invoke();
        else
            btn.Click();

        var sw = Stopwatch.StartNew();
        var started = false;
        var last = Status();
        while (sw.Elapsed < limit)
        {
            last = Status();
            if (last is "running" or "parsing") started = true;
            else if (started && last == "ready")
                return new RunResult(true, true, last, sw.Elapsed);
            Thread.Sleep(500);
        }
        return new RunResult(started, false, last, sw.Elapsed);
    }

    /// <summary>UI ツリーのダンプ (PadProbe.DumpNode と同形式)。</summary>
    public string Dump(int depth)
    {
        var sb = new StringBuilder();
        DumpNode(_window, 0, depth, sb);
        return sb.ToString();
    }

    private void DumpNode(AutomationElement el, int depth, int max, StringBuilder sb)
    {
        var indent = new string(' ', depth * 2);
        var autoId = SafeStr(() => el.Properties.AutomationId.Value);
        var r = el.BoundingRectangle;
        var enabled = !SafeBool(() => !el.IsEnabled);
        sb.AppendLine($"{indent}[{el.ControlType}] '{el.Name}' id='{autoId}' " +
            $"rect={{X={r.X},Y={r.Y},Width={r.Width},Height={r.Height}}}" +
            (enabled ? "" : " DISABLED"));
        if (depth >= max) return;
        foreach (var child in el.FindAllChildren())
            DumpNode(child, depth + 1, max, sb);
    }

    private IReadOnlyList<string> CollectErrorTexts()
    {
        var texts = new List<string>();
        foreach (var item in _window.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem)))
        {
            foreach (var t in item.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)))
            {
                var name = t.Name;
                if (name.Length >= 4 && ErrorTextPattern.IsMatch(name))
                    texts.Add(name);
            }
        }
        return texts;
    }

    private void WaitStatus(string wanted, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Status() == wanted) return;
            Thread.Sleep(500);
        }
        throw new PadDesignerException(
            $"ステータスが '{wanted}' になりません (現在: '{Status()}', {timeout.TotalSeconds} 秒経過)");
    }

    private AutomationElement? FindById(string automationId) =>
        _window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    private static void SendKeys(params VirtualKeyShort[] keys)
    {
        foreach (var k in keys) Keyboard.Press(k);
        foreach (var k in keys.Reverse()) Keyboard.Release(k);
    }

    private static void SetClipboard(string text)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { System.Windows.Forms.Clipboard.SetText(text); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw new PadDesignerException($"クリップボードへの設定に失敗しました: {error.Message}");
    }

    private static IEnumerable<Window> DesignerWindows(UIA3Automation automation)
    {
        foreach (var w in automation.GetDesktop().FindAllChildren())
        {
            if (w.ControlType != ControlType.Window) continue;
            var pid = SafeInt(() => w.Properties.ProcessId.Value);
            if (pid <= 0) continue;
            if (SafeProcName(pid).StartsWith("PAD.Designer", StringComparison.OrdinalIgnoreCase))
                yield return w.AsWindow();
        }
    }

    private static string SafeProcName(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName; } catch { return ""; }
    }

    private static int SafeInt(Func<int> f)
    {
        try { return f(); } catch { return 0; }
    }

    private static string SafeStr(Func<string> f)
    {
        try { return f(); } catch { return ""; }
    }

    private static bool SafeBool(Func<bool> f)
    {
        try { return f(); } catch { return false; }
    }

    public void Dispose() => _automation.Dispose();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
