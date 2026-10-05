using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

// PadProbe — PAD コンソール/デザイナーの UI ツリー探索・操作
//   launch                        : PAD コンソール起動
//   list                          : トップレベルウィンドウ列挙
//   dump <procLike> <depth>       : プロセス名一致ウィンドウの配下をダンプ
//   click <procLike> <autoId>     : AutomationId 一致要素をクリック
//   clickname <procLike> <name>   : Name 一致要素をクリック
//   settext <procLike> <autoId> <text> : Edit 要素へテキスト設定
//   keys <procLike> <vk,...>      : ウィンドウを前面化してキー送信 (例 CONTROL,V)
//   windows                       : PAD 系プロセスのウィンドウ全列挙

var mode = args.Length > 0 ? args[0] : "list";

switch (mode)
{
    case "launch":
        Process.Start(new ProcessStartInfo("ms-power-automate:") { UseShellExecute = true });
        Console.WriteLine("launched ms-power-automate:");
        break;

    case "list":
    case "windows":
        using (var a = new UIA3Automation())
        {
            foreach (var w in a.GetDesktop().FindAllChildren())
            {
                var pid = SafeInt(() => w.Properties.ProcessId.Value) ?? 0;
                var pname = pid > 0 ? SafeProcName(pid) : "";
                if (mode == "list" || pname.Contains("PAD") || pname.Contains("Automate"))
                    Console.WriteLine($"[{w.ControlType}] '{w.Name}' pid={pid} {pname}");
            }
        }
        break;

    case "dump":
        DumpByProc(args[1], int.Parse(args[2]));
        break;

    case "click":
    {
        var el = FindByAutoId(args[1], args[2]);
        if (el is null) { Console.WriteLine($"not found: {args[2]}"); return; }
        el.Click();
        Console.WriteLine($"clicked '{el.Name}' ({args[2]})");
        break;
    }

    case "clickname":
    {
        var el = FindByName(args[1], args[2]);
        if (el is null) { Console.WriteLine($"not found: {args[2]}"); return; }
        el.Click();
        Console.WriteLine($"clicked '{el.Name}'");
        break;
    }

    case "drag":
    {
        // drag <proc> <srcAutomationId> <dstX> <dstY>
        using var a = new UIA3Automation();
        var w = FindByProc(a, args[1]);
        if (w is null) { Console.WriteLine($"window not found: {args[1]}"); return; }
        w.SetForeground();
        Thread.Sleep(300);
        var el = w.FindFirstDescendant(cf => cf.ByAutomationId(args[2]));
        if (el is null) { Console.WriteLine($"not found: {args[2]}"); return; }
        var r = el.BoundingRectangle;
        var from = new System.Drawing.Point((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
        var to = new System.Drawing.Point(int.Parse(args[3]), int.Parse(args[4]));
        FlaUI.Core.Input.Mouse.MoveTo(from);
        Thread.Sleep(300);
        FlaUI.Core.Input.Mouse.Down();
        Thread.Sleep(300);
        FlaUI.Core.Input.Mouse.MoveTo(new System.Drawing.Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        Thread.Sleep(300);
        FlaUI.Core.Input.Mouse.MoveTo(to);
        Thread.Sleep(500);
        FlaUI.Core.Input.Mouse.Up();
        Console.WriteLine($"dragged {args[2]} -> ({to.X},{to.Y})");
        break;
    }

    case "clickitem":
    {
        // clickitem <proc> <listAutomationId> [index] - click Nth ListItem/TreeItem child
        using var a = new UIA3Automation();
        var w = FindByProc(a, args[1]);
        if (w is null) { Console.WriteLine($"window not found: {args[1]}"); return; }
        w.SetForeground();
        Thread.Sleep(300);
        var list = w.FindFirstDescendant(cf => cf.ByAutomationId(args[2]));
        if (list is null) { Console.WriteLine($"not found: {args[2]}"); return; }
        var idx = args.Length > 3 ? int.Parse(args[3]) : 0;
        var items = list.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem)).ToArray();
        if (idx >= items.Length) { Console.WriteLine($"no item {idx} (have {items.Length})"); return; }
        items[idx].Click();
        Console.WriteLine($"clicked item {idx} of {args[2]}");
        break;
    }

    case "dblclick":
    {
        using var a = new UIA3Automation();
        var w = FindByProc(a, args[1]);
        if (w is null) { Console.WriteLine($"window not found: {args[1]}"); return; }
        w.SetForeground();
        Thread.Sleep(300);
        var el = w.FindFirstDescendant(cf => cf.ByAutomationId(args[2]));
        if (el is null) { Console.WriteLine($"not found: {args[2]}"); return; }
        el.DoubleClick();
        Console.WriteLine($"dblclicked {args[2]}");
        break;
    }

    case "rclick":
    {
        var el = FindByAutoId(args[1], args[2]);
        if (el is null) { Console.WriteLine($"not found: {args[2]}"); return; }
        var r = el.BoundingRectangle;
        FlaUI.Core.Input.Mouse.RightClick(new System.Drawing.Point((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2)));
        Console.WriteLine($"right-clicked '{el.Name}'");
        break;
    }

    case "settext":
    {
        using var a = new UIA3Automation();
        var w = FindByProc(a, args[1]);
        if (w is null) { Console.WriteLine($"window not found: {args[1]}"); return; }
        w.SetForeground();
        Thread.Sleep(300);
        var el = w.FindFirstDescendant(cf => cf.ByAutomationId(args[2]));
        if (el is null) { Console.WriteLine($"not found: {args[2]}"); return; }
        el.Focus();
        Thread.Sleep(200);
        if (el.Patterns.Value.IsSupported)
        {
            el.Patterns.Value.Pattern.SetValue(args[3]);
        }
        else
        {
            el.Click();
            FlaUI.Core.Input.Keyboard.Type(args[3]);
        }
        Console.WriteLine($"set '{args[3]}' on {args[2]}");
        break;
    }

    case "clickxy":
    {
        using var a = new UIA3Automation();
        var w = FindByProc(a, args[1]);
        if (w is null) { Console.WriteLine($"window not found: {args[1]}"); return; }
        w.SetForeground();
        Thread.Sleep(400);
        FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(int.Parse(args[2]), int.Parse(args[3])));
        Console.WriteLine($"clicked ({args[2]},{args[3]})");
        break;
    }

    case "keys":
    {
        using var a = new UIA3Automation();
        var w = FindByProc(a, args[1]);
        if (w is null) { Console.WriteLine($"window not found: {args[1]}"); return; }
        w.SetForeground();
        Thread.Sleep(400);
        var keys = args[2].Split(',').Select(k =>
        {
            var s = k.Trim().ToUpper();
            if (s.Length == 1 && char.IsLetterOrDigit(s[0])) s = "KEY_" + s;
            return Enum.Parse<VirtualKeyShort>(s);
        }).ToArray();
        foreach (var k in keys) FlaUI.Core.Input.Keyboard.Press(k);
        foreach (var k in keys.Reverse()) FlaUI.Core.Input.Keyboard.Release(k);
        Console.WriteLine($"sent {args[2]}");
        break;
    }
}

static string SafeProcName(int pid)
{
    try { return Process.GetProcessById(pid).ProcessName; } catch { return ""; }
}

static T? Safe<T>(Func<T> f) where T : struct
{
    try { return f(); } catch { return null; }
}

static int? SafeInt(Func<int> f)
{
    try { return f(); } catch { return null; }
}

static string SafeStr(Func<string> f)
{
    try { return f(); } catch { return ""; }
}

// procLike: "console" → PAD.Console*, "designer" → PAD.Designer*, その他 → プロセス名部分一致
static Window? FindByProc(UIA3Automation a, string procLike)
{
    foreach (var w in a.GetDesktop().FindAllChildren())
    {
        if (w.ControlType != ControlType.Window) continue;
        var pid = SafeInt(() => w.Properties.ProcessId.Value) ?? 0;
        var pname = pid > 0 ? SafeProcName(pid) : "";
        var match = procLike.ToLowerInvariant() switch
        {
            "console" => pname.StartsWith("PAD.Console", StringComparison.OrdinalIgnoreCase),
            "designer" => pname.StartsWith("PAD.Designer", StringComparison.OrdinalIgnoreCase),
            _ => pname.Contains(procLike, StringComparison.OrdinalIgnoreCase)
                 || w.Name.Contains(procLike, StringComparison.OrdinalIgnoreCase),
        };
        if (match) return w.AsWindow();
    }
    return null;
}

static AutomationElement? FindByAutoId(string procLike, string autoId)
{
    using var a = new UIA3Automation();
    var w = FindByProc(a, procLike);
    if (w is null) { Console.WriteLine($"window '{procLike}' not found"); return null; }
    w.SetForeground();
    Thread.Sleep(300);
    return w.FindFirstDescendant(cf => cf.ByAutomationId(autoId));
}

static AutomationElement? FindByName(string procLike, string name)
{
    using var a = new UIA3Automation();
    var w = FindByProc(a, procLike);
    if (w is null) { Console.WriteLine($"window '{procLike}' not found"); return null; }
    w.SetForeground();
    Thread.Sleep(300);
    return w.FindAllDescendants().FirstOrDefault(e =>
        e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
        || e.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
}

static void DumpByProc(string procLike, int depth)
{
    using var a = new UIA3Automation();
    var w = FindByProc(a, procLike);
    if (w is null) { Console.WriteLine($"window '{procLike}' not found"); return; }
    DumpNode(w, 0, depth);
}

static void DumpNode(AutomationElement el, int depth, int max)
{
    var indent = new string(' ', depth * 2);
    var autoId = SafeStr(() => el.Properties.AutomationId.Value);
    var rect = Safe(() => el.BoundingRectangle);
    var enabled = Safe(() => el.Properties.IsEnabled.Value) ?? true;
    var name = SafeStr(() => el.Name);
    Console.WriteLine($"{indent}[{el.ControlType}] '{Trunc(name, 55)}' id='{autoId}' rect={rect}{(enabled ? "" : " DISABLED")}");
    if (depth >= max) return;
    foreach (var c in el.FindAllChildren())
        DumpNode(c, depth + 1, max);
}

static string Trunc(string? s, int n) => s is null ? "" : (s.Length > n ? s[..n] + "…" : s);
