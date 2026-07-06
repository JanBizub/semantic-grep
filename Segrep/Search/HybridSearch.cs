namespace Segrep.Search;

public sealed class HybridSearch(SemanticSearch semantic, FullTextSearch fullText, GrepSearch grep)
{
    // Standard RRF constant — keeps high-rank documents well-separated.
    private const int K = 60;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int perLegLimit = 20,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        var semanticTask = semantic.SearchAsync(query, perLegLimit, cancellationToken);
        var ftsTask = fullText.SearchAsync(query, perLegLimit, cancellationToken);
        var grepTask = grep.SearchAsync(query, perLegLimit, cancellationToken);

        await Task.WhenAll(semanticTask, ftsTask, grepTask);

        var fused = Fuse(
            semanticTask.Result,
            ftsTask.Result,
            grepTask.Result);
        
        return fused.Take(topK).ToList();
    }

    private static IEnumerable<SearchResult> Fuse(
        IReadOnlyList<SearchResult> semanticResults,
        IReadOnlyList<SearchResult> ftsResults,
        IReadOnlyList<SearchResult> grepResults)
    {
        var rrfScores = new Dictionary<long, double>();
        var byId = new Dictionary<long, SearchResult>();

        void AddLeg(IReadOnlyList<SearchResult> results)
        {
            for (int rank = 0; rank < results.Count; rank++)
            {
                var r = results[rank];
                rrfScores.TryGetValue(r.Id, out var existing);
                rrfScores[r.Id] = existing + 1.0 / (K + rank + 1);
                byId.TryAdd(r.Id, r);
            }
        }

        AddLeg(semanticResults);
        AddLeg(ftsResults);
        AddLeg(grepResults);

        return rrfScores
            .OrderByDescending(kv => kv.Value)
            .Select(kv => byId[kv.Key] with { Score = kv.Value });
    }
}
