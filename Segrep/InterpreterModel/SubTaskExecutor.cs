using Segrep.Search;

namespace Segrep.InterpreterModel;

/// <summary>A sub-task's retrieval result rendered as prompt-ready context text.</summary>
public sealed record CompoundSection(QueryTask Task, string Context);

/// <summary>
/// Runs one decomposed <see cref="QueryTask"/> through the retrieval pipeline matching its
/// intent and returns the result as a context section for compound answer composition.
/// </summary>
public sealed class SubTaskExecutor(HybridSearch hybridSearch, SemanticSearch semanticSearch, TermSearch termSearch)
{
    public async Task<CompoundSection> ExecuteAsync(QueryTask task, int topK, CancellationToken cancellationToken = default)
    {
        switch (task.Intent)
        {
            case QueryIntent.ExactTerm:
            {
                var occurrences = await termSearch.FindAsync(task.Query, task.DocumentFilter, cancellationToken);
                return new CompoundSection(task, TermOccurrenceFormatter.BuildSummary(task.Query, occurrences));
            }
            case QueryIntent.CorpusWide:
            {
                var chunks = await semanticSearch.SearchPerDocumentAsync(task.Query, perDocTopK: 3, cancellationToken);
                var documents = ContextFormatter.DocumentNames(chunks);
                var context =
                    $"The database contains {documents.Count} documents: {string.Join(", ", documents)}\n\n" +
                    ContextFormatter.BuildGrouped(chunks);
                return new CompoundSection(task, context);
            }
            default:
            {
                var chunks = await hybridSearch.SearchAsync(task.Query, topK: topK, cancellationToken: cancellationToken);
                return new CompoundSection(task, ContextFormatter.BuildFlat(chunks));
            }
        }
    }
}
