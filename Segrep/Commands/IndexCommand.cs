using Segrep.Chunking;
using Segrep.DocumentIntelligence;
using Segrep.Embeddings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class IndexCommand(
    DocumentParser parser,
    MarkdownChunker chunker,
    EmbeddingPipeline pipeline) : AsyncCommand<IndexCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [System.ComponentModel.Description("Folder or file to index (defaults to current directory).")]
        public string? Path { get; init; }

        [CommandOption("--force")]
        [System.ComponentModel.Description("Re-index files even if content is unchanged.")]
        public bool Force { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var root = settings.Path ?? Directory.GetCurrentDirectory();
        var files = GatherFiles(root);

        if (files.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No supported files found.[/]");
            return 0;
        }

        var totalChunks = 0;
        var processed = 0;
        var skipped = 0;
        var unchanged = 0;

        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Indexing", maxValue: files.Count);

                foreach (var file in files)
                {
                    task.Description = Markup.Escape(System.IO.Path.GetFileName(file));

                    try
                    {
                        var fileBytes = await File.ReadAllBytesAsync(file, cancellationToken);
                        var fileHash = DocumentParser.ComputeHash(fileBytes);

                        if (!settings.Force)
                        {
                            if (await pipeline.IsUpToDateAsync(file, fileHash, cancellationToken))
                            {
                                unchanged++;
                                task.Increment(1);
                                continue;
                            }

                            var duplicatePath = await pipeline.FindDuplicatePathAsync(file, fileHash, cancellationToken);
                            if (duplicatePath is not null)
                            {
                                AnsiConsole.MarkupLine(
                                    $"[yellow]Warning:[/] {Markup.Escape(System.IO.Path.GetFileName(file))} " +
                                    $"has the same content as already-indexed {Markup.Escape(duplicatePath)}");
                            }
                        }

                        var parsed = await parser.ParseAsync(file, cancellationToken);
                        var chunks = chunker.Chunk(file, parsed.Hash, parsed.Markdown);

                        if (chunks.Count > 0)
                        {
                            await pipeline.IngestAsync(chunks, cancellationToken);
                            totalChunks += chunks.Count;
                            processed++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error[/] {Markup.Escape(file)}: {Markup.Escape(ex.Message)}");
                    }

                    task.Increment(1);
                }
            });

        AnsiConsole.MarkupLine($"[green]Done.[/] Processed [bold]{processed}[/] file(s), [bold]{totalChunks}[/] chunk(s). Unchanged [bold]{unchanged}[/]. Skipped [bold]{skipped}[/].");
        return 0;
    }

    private static IReadOnlyList<string> GatherFiles(string root)
    {
        if (File.Exists(root))
            return DocumentParser.IsSupported(root) ? [root] : [];

        if (!Directory.Exists(root))
        {
            AnsiConsole.MarkupLine($"[red]Path not found:[/] {Markup.Escape(root)}");
            return [];
        }

        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(DocumentParser.IsSupported)
            .OrderBy(f => f)
            .ToList();
    }
}
