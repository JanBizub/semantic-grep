using Segrep.Search;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class FindCommand(TermSearch termSearch) : AsyncCommand<FindCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<term>")]
        [System.ComponentModel.Description("The exact word or phrase to count (case-insensitive, whole-word match).")]
        public string Term { get; init; } = string.Empty;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var results = await termSearch.FindAsync(settings.Term, cancellationToken);
        RenderOccurrences(settings.Term, results);
        return 0;
    }

    internal static void RenderOccurrences(string term, IReadOnlyList<TermDocumentOccurrences> results)
    {
        var escapedTerm = Markup.Escape(term);
        if (results.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No occurrences of [bold]{escapedTerm}[/] found in the indexed documents.[/]");
            AnsiConsole.MarkupLine(
                "[grey]Only indexed documents are searched — run [bold]segrep list[/] to see them, " +
                "or [bold]segrep index <path>[/] to add the document you expected.[/]");
            return;
        }

        var total = results.Sum(r => r.TotalCount);
        var table = new Table()
            .Title($"[bold]{escapedTerm}[/] — {total} occurrence(s) in {results.Count} document(s)")
            .AddColumn("Document")
            .AddColumn(new TableColumn("Occurrences").RightAligned())
            .AddColumn("Pages");

        foreach (var doc in results)
        {
            table.AddRow(
                Markup.Escape(Path.GetFileName(doc.FilePath)),
                doc.TotalCount.ToString(),
                Markup.Escape(TermOccurrenceFormatter.FormatPages(doc)));
        }

        AnsiConsole.Write(table);

        if (results.Any(r => r.Approximate))
        {
            AnsiConsole.MarkupLine(
                "[grey]* approximate: the parse cache for this document is missing, so counts come from stored " +
                "chunks and pages are chunk-level. Re-run [bold]index --force[/] for exact numbers.[/]");
        }
    }
}
