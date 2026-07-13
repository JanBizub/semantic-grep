using Segrep.Search;

namespace Segrep.Tests.Search;

public class HybridSearchFusionTests
{
    private const int K = 60;

    private static SearchResult Result(long id, string filePath = "/docs/a.pdf") =>
        new(id, filePath, "hash", (int)id, $"chunk {id}", 0.0);

    [Fact]
    public void ResultInAllLegsOutranksSingleLegResults()
    {
        var shared = Result(1);
        var fused = HybridSearch.Fuse(
            [shared, Result(2)],
            [shared, Result(3)],
            [shared, Result(4)]).ToList();

        Assert.Equal(1, fused[0].Id);
        Assert.Equal(4, fused.Count);
    }

    [Fact]
    public void ScoresAreSumOfReciprocalRanks()
    {
        var shared = Result(1);
        // Rank 0 in the first leg, rank 1 in the second, absent from the third.
        var fused = HybridSearch.Fuse(
            [shared],
            [Result(2), shared],
            []).ToList();

        var expected = 1.0 / (K + 1) + 1.0 / (K + 2);
        Assert.Equal(expected, fused.Single(r => r.Id == 1).Score, precision: 10);
    }

    [Fact]
    public void DuplicatesAcrossLegsAppearOnce()
    {
        var shared = Result(1);
        var fused = HybridSearch.Fuse([shared], [shared], [shared]).ToList();

        Assert.Single(fused);
    }

    [Fact]
    public void EmptyLegsProduceEmptyResult()
    {
        Assert.Empty(HybridSearch.Fuse([], [], []));
    }

    [Fact]
    public void CapPerDocumentLimitsChunksPerFile()
    {
        var ordered = new List<SearchResult>
        {
            Result(1, "/a.pdf"),
            Result(2, "/a.pdf"),
            Result(3, "/a.pdf"),
            Result(4, "/b.pdf"),
            Result(5, "/c.pdf"),
        };

        var capped = HybridSearch.CapPerDocument(ordered, topK: 4, maxPerDocument: 2);

        Assert.Equal([1, 2, 4, 5], capped.Select(r => r.Id));
    }

    [Fact]
    public void CapPerDocumentBackfillsSkippedChunksWhenTopKUnderfilled()
    {
        var ordered = new List<SearchResult>
        {
            Result(1, "/a.pdf"),
            Result(2, "/a.pdf"),
            Result(3, "/a.pdf"),
            Result(4, "/a.pdf"),
        };

        var capped = HybridSearch.CapPerDocument(ordered, topK: 3, maxPerDocument: 2);

        // Single-document questions still fill top-k: the third chunk is backfilled.
        Assert.Equal([1, 2, 3], capped.Select(r => r.Id));
    }

    [Fact]
    public void CapPerDocumentRespectsTopK()
    {
        var ordered = Enumerable.Range(1, 10).Select(i => Result(i, $"/doc{i}.pdf")).ToList();

        var capped = HybridSearch.CapPerDocument(ordered, topK: 5, maxPerDocument: 2);

        Assert.Equal(5, capped.Count);
        Assert.Equal([1, 2, 3, 4, 5], capped.Select(r => r.Id));
    }
}
