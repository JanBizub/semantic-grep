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

        return CapPerDocument(fused.ToList(), topK, maxPerDocument: 2);
    }

    // Keeps one document from monopolizing all top-k slots when several documents are
    // relevant; skipped chunks are backfilled in fused order so single-document
    // questions still fill top-k.
    private static List<SearchResult> CapPerDocument(List<SearchResult> ordered, int topK, int maxPerDocument)
    {
        var counts = new Dictionary<string, int>();
        var taken = new List<SearchResult>(topK);
        var skipped = new List<SearchResult>();

        foreach (var result in ordered)
        {
            if (taken.Count == topK) break;
            counts.TryGetValue(result.FilePath, out var count);
            if (count < maxPerDocument)
            {
                counts[result.FilePath] = count + 1;
                taken.Add(result);
            }
            else
            {
                skipped.Add(result);
            }
        }

        foreach (var result in skipped)
        {
            if (taken.Count == topK) break;
            taken.Add(result);
        }

        return taken;
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
