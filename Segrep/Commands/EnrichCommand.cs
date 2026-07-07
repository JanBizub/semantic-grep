using Segrep.InterpreterModel;
using Segrep.Search;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class EnrichCommand(
    HybridSearch hybridSearch,
    SemanticSearch semanticSearch,
    TermSearch termSearch,
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
            ? new PromptInterpretation(QueryIntent.Focused, settings.Prompt)
            : await interpreter.InterpretPromptAsync(settings.Prompt, cancellationToken);
        if (interpretation.Intent == QueryIntent.ExactTerm)
        {
            var occurrences = await termSearch.FindAsync(interpretation.ExpandedQuery, cancellationToken);
            Console.Write(BuildTermPrompt(settings.Prompt, interpretation.ExpandedQuery, occurrences));
            return 0;
        }

        var corpusWide = interpretation.Intent == QueryIntent.CorpusWide;

        var chunks = corpusWide
            ? await semanticSearch.SearchPerDocumentAsync(interpretation.ExpandedQuery, perDocTopK: 3, cancellationToken)
            : await hybridSearch.SearchAsync(interpretation.ExpandedQuery, topK: settings.TopK, cancellationToken: cancellationToken);

        // Emit an augmented prompt to stdout, ready to pipe into another LLM call.
        Console.Write(BuildAugmentedPrompt(settings.Prompt, chunks, corpusWide));
        return 0;
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
