using System.ComponentModel;
using Segrep.Store;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class DocumentClearCommand(DocumentStore store) : AsyncCommand<DocumentClearCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-y|--yes")]
        [Description("Skip confirmation prompt.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!settings.Yes)
        {
            var count = await store.CountAsync(cancellationToken);
            if (!AnsiConsole.Confirm($"This will delete all {count} stored document(s). Continue?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[grey]Aborted.[/]");
                return 0;
            }
        }

        var deleted = await store.ClearAsync(cancellationToken);

        AnsiConsole.MarkupLine($"[green]Done.[/] Deleted [bold]{deleted}[/] document(s).");
        return 0;
    }
}
