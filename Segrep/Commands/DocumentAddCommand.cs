using System.ComponentModel;
using Segrep.Documents;
using Segrep.Store;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class DocumentAddCommand(DocumentStore store, MarkdownDocumentParser parser) : AsyncCommand<DocumentAddCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to the markdown document to store.")]
        public string Path { get; init; } = string.Empty;

        public override ValidationResult Validate() =>
            File.Exists(Path)
                ? ValidationResult.Success()
                : ValidationResult.Error($"File not found: {Path}");
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            var markdown = await File.ReadAllTextAsync(settings.Path, cancellationToken);
            var document = parser.Parse(markdown);
            var (id, replaced) = await store.AddAsync(document, cancellationToken);

            AnsiConsole.MarkupLine($"[green]Stored[/] [bold]{Markup.Escape(document.Name)}[/] ({document.TotalSectionCount} section(s)) as [bold]{id}[/].");
            if (replaced)
            {
                AnsiConsole.MarkupLine("[yellow]Replaced a previous version of this document.[/]");
            }

            return 0;
        }
        catch (DocumentFormatException ex)
        {
            AnsiConsole.MarkupLine($"[red]Invalid document structure[/] in {Markup.Escape(settings.Path)}:");
            foreach (var error in ex.Errors)
            {
                AnsiConsole.MarkupLine($"  [red]line {error.Line}:[/] {Markup.Escape(error.Message)}");
            }

            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AnsiConsole.MarkupLine($"[red]Error[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
