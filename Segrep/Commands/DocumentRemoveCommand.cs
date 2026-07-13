using System.ComponentModel;
using Segrep.Store;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class DocumentRemoveCommand(DocumentStore store) : AsyncCommand<DocumentRemoveCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<id>")]
        [Description("Id (Guid) of the document to remove, as shown by 'document list'.")]
        public string Id { get; init; } = string.Empty;

        public override ValidationResult Validate() =>
            Guid.TryParse(Id, out _)
                ? ValidationResult.Success()
                : ValidationResult.Error($"'{Id}' is not a valid document id (Guid).");
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var id = Guid.Parse(settings.Id);
        if (!await store.RemoveAsync(id, cancellationToken))
        {
            AnsiConsole.MarkupLine($"[red]Document not found:[/] {id}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Removed[/] document {id}.");
        return 0;
    }
}
