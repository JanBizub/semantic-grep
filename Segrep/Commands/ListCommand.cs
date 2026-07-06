using Segrep.Embeddings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class ListCommand(EmbeddingPipeline pipeline) : AsyncCommand
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var documents = await pipeline.ListDocumentsAsync(cancellationToken);

        if (documents.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No documents indexed yet.[/]");
            return 0;
        }

        var table = new Table().Title("Indexed Documents").Border(TableBorder.Rounded);
        table.AddColumn("File");
        table.AddColumn("Hash");
        table.AddColumn(new TableColumn("Chunks").RightAligned());

        foreach (var (fileName, fileHash, chunkCount) in documents)
        {
            table.AddRow(
                Markup.Escape(fileName),
                fileHash[..12],
                chunkCount.ToString());
        }

        AnsiConsole.Write(table);

        return 0;
    }
}
