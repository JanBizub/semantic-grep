using Segrep.Store;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class DocumentListCommand(DocumentStore store) : AsyncCommand
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var documents = await store.ListAsync(cancellationToken);

        if (documents.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No documents.[/]");
            return 0;
        }

        var table = new Table().Title("Structured Documents").Border(TableBorder.Rounded);
        table.AddColumn("Id");
        table.AddColumn("Name");

        foreach (var (id, name, _) in documents)
        {
            table.AddRow(id.ToString(), Markup.Escape(name));
        }

        AnsiConsole.Write(table);

        return 0;
    }
}
