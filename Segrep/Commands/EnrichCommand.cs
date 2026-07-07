using Segrep.InterpreterModel;
using Segrep.Search;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class EnrichCommand(
    HybridSearch hybridSearch,
    SemanticSearch semanticSearch,
    TermSearch termSearch,
    SubTaskExecutor subTaskExecutor,
    InterpreterService interpreter)
    : AsyncCommand<EnrichCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<prompt>")]
        [System.ComponentModel.Description("The prompt to augment with retrieved context.")]
        public string Prompt { get; init; } = string.Empty;

        [CommandOption("--raw")]
        [System.ComponentModel.Description("Embed the prompt verbatim without LLM query expansion.")]
        public bool Raw { get; init; }

        [CommandOption("--top-k <N>")]
        [System.ComponentModel.Description("Number of document chunks to retrieve (default: 5).")]
        public int TopK { get; init; } = 5;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var interpretation = settings.Raw
            ? PromptInterpretation.Single(QueryIntent.Focused, settings.Prompt)
            : await interpreter.InterpretPromptAsync(settings.Prompt, cancellationToken);

        // Emit an augmented prompt to stdout, ready to pipe into another LLM call.
        var augmented = interpretation.Tasks.Count == 1
            ? await BuildSingleAsync(settings, interpretation.Tasks[0], cancellationToken)
            : await BuildCompoundAsync(settings, interpretation.Tasks, cancellationToken);
        Console.Write(augmented);
        return 0;
    }

    private async Task<string> BuildSingleAsync(Settings settings, QueryTask task, CancellationToken cancellationToken)
    {
        if (task.Intent == QueryIntent.ExactTerm)
        {
            var occurrences = await termSearch.FindAsync(task.Query, task.DocumentFilter, cancellationToken);
            return BuildTermPrompt(settings.Prompt, task.Query, occurrences);
        }

        var corpusWide = task.Intent == QueryIntent.CorpusWide;

        var chunks = corpusWide
            ? await semanticSearch.SearchPerDocumentAsync(task.Query, perDocTopK: 3, cancellationToken)
            : await hybridSearch.SearchAsync(task.Query, topK: settings.TopK, cancellationToken: cancellationToken);

        return BuildAugmentedPrompt(settings.Prompt, chunks, corpusWide);
    }

    private async Task<string> BuildCompoundAsync(
        Settings settings,
        IReadOnlyList<QueryTask> tasks,
        CancellationToken cancellationToken)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("The following context sections are provided, one per part of the prompt:");
        sb.AppendLine();
        for (var i = 0; i < tasks.Count; i++)
        {
            var section = await subTaskExecutor.ExecuteAsync(tasks[i], settings.TopK, cancellationToken);
            var label = tasks[i].Intent switch
            {
                QueryIntent.ExactTerm => "exact occurrence counts — authoritative",
                QueryIntent.CorpusWide => "excerpts from every indexed document",
                _ => "document excerpts",
            };
            sb.AppendLine($"## Part {i + 1}: {section.Task.Question} ({label})");
            sb.AppendLine();
            sb.AppendLine(section.Context.TrimEnd());
            sb.AppendLine();
        }
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(settings.Prompt);
        return sb.ToString();
    }

    private static string BuildTermPrompt(string originalPrompt, string term, IReadOnlyList<TermDocumentOccurrences> occurrences)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("The following exact-occurrence counts are provided as context:");
        sb.AppendLine();
        sb.AppendLine(TermOccurrenceFormatter.BuildSummary(term, occurrences));
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(originalPrompt);
        return sb.ToString();
    }

    private static string BuildAugmentedPrompt(string originalPrompt, IReadOnlyList<SearchResult> chunks, bool corpusWide)
    {
        if (chunks.Count == 0)
            return originalPrompt;

        var sb = new System.Text.StringBuilder();
        if (corpusWide)
        {
            var documents = ContextFormatter.DocumentNames(chunks);
            sb.AppendLine("The following excerpts cover every document indexed in the database, grouped per file.");
            sb.AppendLine($"The database contains {documents.Count} documents: {string.Join(", ", documents)}");
            sb.AppendLine();
            sb.Append(ContextFormatter.BuildGrouped(chunks));
        }
        else
        {
            sb.AppendLine("The following document excerpts are provided as context:");
            sb.AppendLine();
            sb.Append(ContextFormatter.BuildFlat(chunks));
        }
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(originalPrompt);
        return sb.ToString();
    }
}
