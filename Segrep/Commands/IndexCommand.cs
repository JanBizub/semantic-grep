using System.Collections.Concurrent;
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

        [CommandOption("--parallel <N>")]
        [System.ComponentModel.Description("How many files to ingest concurrently.")]
        public int Parallelism { get; init; } = 4;

        public override ValidationResult Validate() =>
            Parallelism < 1
                ? ValidationResult.Error("--parallel must be at least 1.")
                : ValidationResult.Success();
    }

    // Per-file progress stage boundaries (task maxValue is 100).
    private const double HashCheckedValue = 10;
    private const double ParsedValue = 65;
    private const double ChunkedValue = 70;

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
        var messages = new ConcurrentQueue<string>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var escListener = StartEscListener(cts);
        if (escListener is not null)
            AnsiConsole.MarkupLine("[grey]Press [bold]Esc[/] to cancel.[/]");

        var cancelled = false;
        try
        {
            await AnsiConsole.Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var tasks = files.ToDictionary(
                        file => file,
                        file => ctx.AddTask(Markup.Escape(System.IO.Path.GetFileName(file)), maxValue: 100));

                    try
                    {
                        await Parallel.ForEachAsync(
                            files,
                            new ParallelOptions
                            {
                                MaxDegreeOfParallelism = settings.Parallelism,
                                CancellationToken = cts.Token,
                            },
                            async (file, ct) =>
                            {
                                var task = tasks[file];
                                var name = Markup.Escape(System.IO.Path.GetFileName(file));

                                try
                                {
                                    var outcome = await IndexFileAsync(file, task, name, settings.Force, messages, ct);
                                    switch (outcome.Kind)
                                    {
                                        case OutcomeKind.Processed:
                                            Interlocked.Increment(ref processed);
                                            Interlocked.Add(ref totalChunks, outcome.ChunkCount);
                                            Finish(task, $"[green]✓[/] {name} ({outcome.ChunkCount} chunks)");
                                            break;
                                        case OutcomeKind.Unchanged:
                                            Interlocked.Increment(ref unchanged);
                                            Finish(task, $"[grey]{name} (unchanged)[/]");
                                            break;
                                        case OutcomeKind.Empty:
                                            Interlocked.Increment(ref skipped);
                                            Finish(task, $"[yellow]{name} (no content)[/]");
                                            break;
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    Finish(task, $"[grey]{name} (cancelled)[/]");
                                }
                                catch (Exception ex)
                                {
                                    messages.Enqueue($"[red]Error[/] {Markup.Escape(file)}: {Markup.Escape(ex.Message)}");
                                    Finish(task, $"[red]✗ {name}[/]");
                                }
                            });
                    }
                    catch (OperationCanceledException)
                    {
                        // Whole-run cancellation (Esc or Ctrl+C); partial results are kept.
                    }

                    foreach (var (file, task) in tasks)
                    {
                        if (!task.IsFinished)
                            Finish(task, $"[grey]{Markup.Escape(System.IO.Path.GetFileName(file))} (cancelled)[/]");
                    }
                });

            cancelled = cts.Token.IsCancellationRequested;
        }
        finally
        {
            // Stops the Esc listener; the run outcome was already captured in `cancelled`.
            cts.Cancel();
            if (escListener is not null)
                await escListener;
        }

        foreach (var message in messages)
            AnsiConsole.MarkupLine(message);

        var summary =
            $"Processed [bold]{processed}[/] file(s), [bold]{totalChunks}[/] chunk(s). " +
            $"Unchanged [bold]{unchanged}[/]. Skipped [bold]{skipped}[/].";

        if (cancelled)
        {
            AnsiConsole.MarkupLine($"[yellow]Cancelled.[/] {summary}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Done.[/] {summary}");
        return 0;
    }

    private enum OutcomeKind { Processed, Unchanged, Empty }

    private readonly record struct Outcome(OutcomeKind Kind, int ChunkCount = 0);

    private async Task<Outcome> IndexFileAsync(
        string file,
        ProgressTask task,
        string name,
        bool force,
        ConcurrentQueue<string> messages,
        CancellationToken cancellationToken)
    {
        task.Description = $"{name} [grey]— checking[/]";
        var fileBytes = await File.ReadAllBytesAsync(file, cancellationToken);
        var fileHash = DocumentParser.ComputeHash(fileBytes);

        if (!force)
        {
            if (await pipeline.IsUpToDateAsync(file, fileHash, cancellationToken))
                return new Outcome(OutcomeKind.Unchanged);

            var duplicatePath = await pipeline.FindDuplicatePathAsync(file, fileHash, cancellationToken);
            if (duplicatePath is not null)
            {
                messages.Enqueue(
                    $"[yellow]Warning:[/] {name} has the same content as already-indexed {Markup.Escape(duplicatePath)}");
            }
        }

        task.Value = HashCheckedValue;
        task.Description = $"{name} [grey]— parsing[/]";
        var parsed = await parser.ParseAsync(file, cancellationToken);

        task.Value = ParsedValue;
        task.Description = $"{name} [grey]— chunking[/]";
        var chunks = chunker.Chunk(file, parsed.Hash, parsed.Markdown);

        task.Value = ChunkedValue;
        if (chunks.Count == 0)
            return new Outcome(OutcomeKind.Empty);

        task.Description = $"{name} [grey]— embedding[/]";
        var ingestProgress = new Progress<double>(fraction =>
        {
            task.Value = ChunkedValue + (100 - ChunkedValue) * fraction;
            if (fraction >= 0.5)
                task.Description = $"{name} [grey]— storing[/]";
        });
        await pipeline.IngestAsync(chunks, ingestProgress, cancellationToken);

        return new Outcome(OutcomeKind.Processed, chunks.Count);
    }

    private static void Finish(ProgressTask task, string description)
    {
        task.Description = description;
        task.Value = task.MaxValue;
        task.StopTask();
    }

    private static Task? StartEscListener(CancellationTokenSource cts)
    {
        if (Console.IsInputRedirected)
            return null;

        return Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Escape)
                {
                    cts.Cancel();
                    return;
                }

                try
                {
                    await Task.Delay(50, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
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
