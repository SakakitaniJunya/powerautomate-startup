namespace FinCalc.Cli.Commands;

/// <summary>コマンド名 → <see cref="ICommand"/> のレジストリ。</summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _map = new(StringComparer.OrdinalIgnoreCase);

    private CommandRegistry(IEnumerable<ICommand> commands)
    {
        foreach (var c in commands) _map[c.Name] = c;
    }

    public static CommandRegistry CreateDefault() => new(new ICommand[]
    {
        new DepScheduleCommand(),
        new RatesCommand(),
        new ExcelDepBatchCommand(),
        new ExcelInvoiceCommand(),
        new ExcelReadCommand(),
        new ExcelInitSampleCommand(),
        new TaxConsumptionCommand(),
        new TaxConsumptionNetCommand(),
        new TaxInvoiceCommand(),
        new TaxWithholdingCommand(),
        new TaxCorporateCommand(),
    });

    public IReadOnlyCollection<ICommand> All => _map.Values;

    public ICommand Resolve(string name) =>
        _map.TryGetValue(name, out var c)
            ? c
            : throw new ArgumentException($"不明なコマンド: '{name}'");

    /// <summary>コマンドを追加/上書きした新しいレジストリを返す（拡張用）。</summary>
    public CommandRegistry With(params ICommand[] commands) =>
        new(_map.Values.Where(c => !commands.Any(x => x.Name == c.Name)).Concat(commands));
}
