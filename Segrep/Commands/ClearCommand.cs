using System.ComponentModel;
using Npgsql;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class ClearCommand(NpgsqlDataSource dataSource) : AsyncCommand<ClearCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-y|--yes")]
        [Description("Skip confirmation prompt.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!settings.Yes && !AnsiConsole.Confirm("This will delete all indexed chunks. Continue?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[grey]Aborted.[/]");
            return 0;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM ai_doc_chunk";
        var deleted = await cmd.ExecuteNonQueryAsync(cancellationToken);

        AnsiConsole.MarkupLine($"[green]Done.[/] Deleted [bold]{deleted}[/] chunk(s).");
        return 0;
    }
}
