using Segrep.InterpreterModel;
using Segrep.Search;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class AskCommand(
    HybridSearch hybridSearch,
    SemanticSearch semanticSearch,
    TermSearch termSearch,
    SubTaskExecutor subTaskExecutor,
    InterpreterService interpreter)
    : AsyncCommand<AskCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<prompt>")]
        [System.ComponentModel.Description("The question to answer from indexed documents.")]
        public string Prompt { get; init; } = string.Empty;

        [CommandOption("--top-k <N>")]
        [System.ComponentModel.Description("Number of document chunks to retrieve (default: 5).")]
        public int TopK { get; init; } = 5;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var interpretation = await interpreter.InterpretPromptAsync(settings.Prompt, cancellationToken);

        return interpretation.Tasks.Count == 1
            ? await ExecuteSingleAsync(settings, interpretation.Tasks[0], cancellationToken)
            : await ExecuteCompoundAsync(settings, interpretation.Tasks, cancellationToken);
    }

    private async Task<int> ExecuteSingleAsync(Settings settings, QueryTask task, CancellationToken cancellationToken)
    {
        if (task.Intent == QueryIntent.ExactTerm)
        {
            AnsiConsole.MarkupLine(
                $"[grey]exact-term search — counting literal occurrences of \"{Markup.Escape(task.Query)}\"" +
                $"{DocumentSuffix(task)}[/]");
            var occurrences = await termSearch.FindAsync(task.Query, task.DocumentFilter, cancellationToken);
            FindCommand.RenderOccurrences(task.Query, occurrences);
            return 0;
        }

        var corpusWide = task.Intent == QueryIntent.CorpusWide;

        if (corpusWide)
            AnsiConsole.MarkupLine("[grey]corpus-wide question — retrieving from every document[/]");

        var chunks = corpusWide
            ? await semanticSearch.SearchPerDocumentAsync(task.Query, perDocTopK: 3, cancellationToken)
            : await hybridSearch.SearchAsync(task.Query, topK: settings.TopK, cancellationToken: cancellationToken);

        if (chunks.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No relevant content found in the indexed documents.[/]");
            return 0;
        }

        var answer = corpusWide
            ? await interpreter.ComposeCorpusAnswerAsync(settings.Prompt, chunks, cancellationToken)
            : await interpreter.ComposeAnswerAsync(settings.Prompt, chunks, cancellationToken);

        RenderQuestionAndAnswer(settings.Prompt, answer);
        return 0;
    }

    private async Task<int> ExecuteCompoundAsync(
        Settings settings,
        IReadOnlyList<QueryTask> tasks,
        CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"[grey]compound question — {tasks.Count} sub-tasks[/]");

        var sections = new List<CompoundSection>(tasks.Count);
        foreach (var task in tasks)
        {
            var description = task.Intent switch
            {
                QueryIntent.ExactTerm => $"exact-term: counting \"{Markup.Escape(task.Query)}\"{DocumentSuffix(task)}",
                QueryIntent.CorpusWide => "corpus-wide: retrieving from every document",
                _ => $"focused: searching \"{Markup.Escape(task.Query)}\"",
            };
            AnsiConsole.MarkupLine($"[grey]  → {description}[/]");
            sections.Add(await subTaskExecutor.ExecuteAsync(task, settings.TopK, cancellationToken));
        }

        var answer = await interpreter.ComposeCompoundAnswerAsync(settings.Prompt, sections, cancellationToken);
        RenderQuestionAndAnswer(settings.Prompt, answer);
        return 0;
    }

    private static string DocumentSuffix(QueryTask task) =>
        task.DocumentFilter is null ? string.Empty : $" in documents matching \"{Markup.Escape(task.DocumentFilter)}\"";

    private static void RenderQuestionAndAnswer(string prompt, string answer)
    {
        var questionPanel = new Panel(new Markup($"[blue]{Markup.Escape(prompt)}[/]"))
            .Header("[bold blue]Question[/]")
            .BorderColor(Color.Blue)
            .Expand();
        AnsiConsole.Write(questionPanel);

        var answerPanel = new Panel(new Markup(Markup.Escape(answer)))
            .Header("[bold]Answer[/]")
            .Expand();
        AnsiConsole.Write(answerPanel);
    }
}
