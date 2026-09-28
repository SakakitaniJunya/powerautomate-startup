using PadKit.Flow;

namespace PadKit.Tests;

/// <summary>examples/ のレシピを sample プロファイルでレンダリングし、lint 無指摘で
/// 主要アクション行を含むことを確認する回帰テスト。</summary>
public class GoldenTests
{
    private static readonly string Root = RepoRoot();
    private static readonly string ExamplesDir = Path.Combine(Root, "examples");
    private static readonly string SampleProfile =
        Path.Combine(ExamplesDir, "profiles", "sample.json");
    private static readonly RuleSet Rules =
        RuleSet.Load(Path.Combine(Root, "rules", "pad-11.2609.json"));

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PadKit.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root (PadKit.sln) not found");
    }

    private static RenderResult Render(string recipePath)
    {
        var profile = Profile.Load(SampleProfile);
        var blocks = BlockResolver.Create(Path.GetDirectoryName(recipePath),
            new[] { Path.Combine(Root, "blocks") });
        return Renderer.Render(File.ReadAllText(recipePath), profile, blocks);
    }

    [Fact]
    public void AllExamples_Render_WithZeroLintFindings()
    {
        var recipes = Directory.GetFiles(ExamplesDir, "*.pad");
        Assert.Equal(7, recipes.Length);
        foreach (var recipe in recipes)
        {
            var result = Render(recipe);
            var findings = Linter.Lint(result.Text, Rules);
            Assert.True(findings.Count == 0,
                $"{Path.GetFileName(recipe)}: {string.Join("; ", findings.Select(f => $"{f.RuleId}@{f.Line}"))}");
        }
    }

    public static IEnumerable<object[]> KeyLines()
    {
        yield return new object[] { "01-run-cli-to-csv",
            new[] { "Scripting.RunDOSCommand.RunDOSCommand", "File.WriteText File: OutPath" } };
        yield return new object[] { "02-excel-rows",
            new[] { "Excel.LaunchExcel.LaunchAndOpen Path: LedgerPath", "Excel.CloseExcel.CloseAndSave" } };
        yield return new object[] { "03-csv-to-json-args",
            new[] { "File.ReadFromCSVFile.ReadCSV CSVFile: CsvPath", "CustomObject=> Summary" } };
        yield return new object[] { "04-csv-folder-batch",
            new[] { "Folder.GetFiles Folder: InDir", "File.Move Files: CsvFile Destination: DoneDir" } };
        yield return new object[] { "05-retry-and-log",
            new[] { "LOOP WHILE (CliExit <> 0) AND (RetryCount <= MaxRetry)", "WAIT 2" } };
        yield return new object[] { "06-teams-notify",
            new[] { "Web.InvokeWebService.InvokeWebService Url: WebhookUrl", "EncodeRequestBody: False" } };
        yield return new object[] { "07-teams-status-card",
            new[] { "\"style\":\"good\"", "\"style\":\"attention\"" } };
    }

    [Theory]
    [MemberData(nameof(KeyLines))]
    public void Example_Output_ContainsKeyActions(string name, string[] expected)
    {
        var result = Render(Path.Combine(ExamplesDir, name + ".pad"));
        foreach (var e in expected)
            Assert.Contains(e, result.Text);
    }

    [Fact]
    public void AllBlocks_Parse()
    {
        var blocks = new BlockResolver(new[] { Path.Combine(Root, "blocks") }).ListAll();
        Assert.Equal(14, blocks.Count);
        Assert.All(blocks, b => Assert.NotEmpty(b.Body));
        Assert.Contains(blocks, b => b.Name == "run-cli"
            && b.Params.Any(p => p.Name == "Args" && p.Required));
        Assert.Contains(blocks, b => b.Name == "info-dialog"
            && b.Params.Any(p => p.Name == "Timeout" && p.Default == "10"));
    }
}
