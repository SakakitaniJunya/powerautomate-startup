using PadKit.Flow;

namespace PadKit.Tests;

public class LinterTests
{
    private static readonly RuleSet Rules = RuleSet.Load(RuleSetPath());

    private static string RuleSetPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, "rules", "pad-11.2609.json");
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("rules/pad-11.2609.json not found");
    }

    private static bool Hits(string text, string ruleId) =>
        Linter.Lint(text, Rules).Any(f => f.RuleId == ruleId);

    private static bool Clean(string text, string ruleId) => !Hits(text, ruleId);

    // (good, bad) のペアで各ルールを検証
    public static IEnumerable<object[]> RuleCases()
    {
        yield return Case("PAD001",
            "Scripting.RunDOSCommand.RunDOSCommand DOSCommandOrApplication: $'''x'''",
            "System.RunDOSCommand DOSCommandOrApplication: $'''x'''");
        yield return Case("PAD002",
            "Scripting.RunDOSCommand.RunDOSCommand DOSCommandOrApplication: $'''x'''\nFile.WriteText File: X TextToWrite: Y AppendNewLine: False IfFileExists: File.IfFileExists.Overwrite Encoding: File.FileEncoding.UTF8",
            "Foo.Bar Arg: 1");
        yield return Case("PAD003",
            "Display.ShowMessageDialog.ShowMessageWithTimeout Icon: Display.Icon.Warning",
            "Display.ShowMessageDialog.ShowMessageWithTimeout Icon: Display.Icon.Error");
        yield return Case("PAD004",
            "File.ReadFromCSVFile.ReadCSV CSVFile: P",
            "File.ReadFromCSVFile.ReadCSV CSVFilePath: P");
        yield return Case("PAD005",
            "File.WriteText File: P TextToWrite: T",
            "File.WriteText FilePath: P TextToWrite: T");
        yield return Case("PAD006",
            "SET X TO G.rate * 100",
            "SET X TO %G.rate * 100%");
        yield return Case("PAD007",
            "SET S TO $'''税率 %RatePct%％'''", // 全角 ％ は OK
            "SET S TO $'''税率 %RatePct%%'''");
        yield return Case("PAD008",
            "LOOP WHILE (CliExit <> 0)\nEND",
            "ON BLOCK ERROR\nEND");
        yield return Case("PAD009",
            "LOOP FOREACH X IN Y\n    SET RowIndex TO RowIndex + 1\nEND",
            "LOOP FOREACH X IN Y\n    SET Z TO LoopIndex\nEND");
        yield return Case("PAD010",
            "Excel.ReadFromExcel.ReadAllCells Instance: Excel FirstLineIsHeader: True RangeValue=> A",
            "Excel.ReadFromExcel.ReadAllCells Instance: Excel FirstLineContainsColumnNames: True RangeValue=> A");
        yield return Case("PAD011",
            "System.RunApplication.RunApplication ApplicationPath: P ProcessId=> A",
            "System.RunApplication.RunApplication ApplicationPath: P Timeout: 30 ProcessId=> A");
        yield return Case("PAD012",
            "Web.InvokeWebService.InvokeWebService Url: U EncodeRequestBody: False",
            "Web.InvokeWebService.InvokeWebService Url: U EncodeRequestBody: True");
        yield return Case("PAD013",
            "Excel.CloseExcel.CloseAndSave Instance: Excel",
            "Excel.CloseExcel.Close Instance: Excel");
    }

    private static object[] Case(string id, string good, string bad) =>
        new object[] { id, good, bad };

    [Theory]
    [MemberData(nameof(RuleCases))]
    public void Rule_PositiveAndNegative(string ruleId, string good, string bad)
    {
        Assert.True(Hits(bad, ruleId), $"{ruleId}: bad case not flagged");
        Assert.True(Clean(good, ruleId), $"{ruleId}: good case flagged");
    }

    [Fact]
    public void Scope_Code_DoesNotSeeInsideString()
    {
        // 文字列中の SET X TO %...% は PAD006 (code scope) に引っかからない
        Assert.True(Clean("SET S TO $'''SET X TO %abc%'''", "PAD006"));
    }

    [Fact]
    public void Scope_String_OnlySeesInsideString()
    {
        Assert.True(Hits("SET S TO $'''%x%%'''", "PAD007"));
        Assert.True(Clean("SET Y TO %x%%", "PAD007"));
        // ただし SET Y TO %... 自体は PAD006
        Assert.True(Hits("SET Y TO %x%%", "PAD006"));
    }

    [Fact]
    public void Comments_AreSkipped()
    {
        Assert.True(Clean("# ON BLOCK ERROR は使わない", "PAD008"));
        Assert.True(Clean("# LoopIndex は使えない", "PAD009"));
    }

    [Fact]
    public void PAD100_MissingEnd_StrayEnd_ElseOutsideIf()
    {
        Assert.True(Hits("IF X = 1 THEN\n    SET Y TO 1\n", "PAD100"));       // 閉じ忘れ
        Assert.True(Hits("SET Y TO 1\nEND\n", "PAD100"));                    // 迷子 END
        Assert.True(Hits("ELSE\n    SET Y TO 1\nEND\n", "PAD100"));          // IF 外 ELSE
        Assert.True(Clean("IF X = 1 THEN\n    SET Y TO 1\nELSE\n    SET Y TO 2\nEND\n", "PAD100"));
        Assert.True(Clean("LOOP FOREACH X IN Y\n    IF X > 0 THEN\n        SET Z TO 1\n    END\nEND\n", "PAD100"));
    }

    [Fact]
    public void PAD101_UnresolvedParam()
    {
        Assert.True(Hits("SET X TO {{Foo}}", "PAD101"));
        Assert.True(Clean("SET X TO 1", "PAD101"));
    }
}
