using Segrep.Chunking;
using Segrep.DocumentIntelligence;

namespace Segrep.Tests.Chunking;

public class MarkdownChunkerTests
{
    private readonly MarkdownChunker _chunker = new();

    private IReadOnlyList<Chunk> Chunk(string markdown, PageMap? pages = null) =>
        _chunker.Chunk("/docs/file.md", "hash", markdown, pages);

    [Fact]
    public void SmallDocumentProducesSingleChunk()
    {
        var chunks = Chunk("# Title\n\nA short paragraph.");

        var chunk = Assert.Single(chunks);
        Assert.Equal("/docs/file.md", chunk.FilePath);
        Assert.Equal("hash", chunk.FileHash);
        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Contains("# Title", chunk.Text);
        Assert.Contains("A short paragraph.", chunk.Text);
    }

    [Fact]
    public void EmptyMarkdownProducesNoChunks()
    {
        Assert.Empty(Chunk(""));
        Assert.Empty(Chunk("   \n\n  \n"));
    }

    [Fact]
    public void LongDocumentIsSplitIntoMultipleChunksWithSequentialIndexes()
    {
        var paragraphs = Enumerable.Range(0, 30)
            .Select(i => $"Paragraph {i}: " + string.Join(' ', Enumerable.Repeat($"word{i}", 60)));
        var markdown = string.Join("\n\n", paragraphs);

        var chunks = Chunk(markdown);

        Assert.True(chunks.Count > 1);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.ChunkIndex));
    }

    [Fact]
    public void ConsecutiveChunksOverlap()
    {
        var paragraphs = Enumerable.Range(0, 30)
            .Select(i => $"Paragraph {i}: " + string.Join(' ', Enumerable.Repeat($"word{i}", 60)));
        var markdown = string.Join("\n\n", paragraphs);

        var chunks = Chunk(markdown);

        Assert.True(chunks.Count > 1);
        for (var i = 1; i < chunks.Count; i++)
        {
            // The head of each chunk repeats the tail of the previous one.
            var head = chunks[i].Text[..Math.Min(100, chunks[i].Text.Length)];
            Assert.Contains(head, chunks[i - 1].Text);
        }
    }

    [Fact]
    public void EveryChunkTextIsTrimmedAndNonEmpty()
    {
        var markdown = "# H\n\n" + string.Join("\n\n", Enumerable.Range(0, 20)
            .Select(i => string.Join(' ', Enumerable.Repeat($"w{i}", 80))));

        foreach (var chunk in Chunk(markdown))
        {
            Assert.False(string.IsNullOrWhiteSpace(chunk.Text));
            Assert.Equal(chunk.Text, chunk.Text.Trim());
        }
    }

    [Fact]
    public void HeadingsWithoutBlankLinesAreSplitIntoSeparateBlocks()
    {
        // Two headings joined by single newlines form one "block" that must be
        // re-split at the second heading.
        var markdown = "# First\nbody one\n## Second\nbody two";

        var chunks = Chunk(markdown);

        var text = string.Concat(chunks.Select(c => c.Text));
        Assert.Contains("# First", text);
        Assert.Contains("## Second", text);
    }

    [Fact]
    public void PageRangeIsAttributedFromPageMap()
    {
        var first = string.Join(' ', Enumerable.Repeat("alpha", 100));
        var second = string.Join(' ', Enumerable.Repeat("beta", 100));
        var markdown = first + "\n\n" + second;

        var pages = PageMap.FromSpans(
        [
            new PageSpan(1, 0, first.Length),
            new PageSpan(2, first.Length + 2, second.Length),
        ]);

        var chunks = Chunk(markdown, pages);

        var chunk = Assert.Single(chunks);
        Assert.Equal(1, chunk.PageStart);
        Assert.Equal(2, chunk.PageEnd);
    }

    [Fact]
    public void WithoutPageMapPageColumnsAreNull()
    {
        var chunk = Assert.Single(Chunk("some text"));
        Assert.Null(chunk.PageStart);
        Assert.Null(chunk.PageEnd);
    }

    [Fact]
    public void AllContentIsPreservedAcrossChunks()
    {
        var paragraphs = Enumerable.Range(0, 25)
            .Select(i => $"Unique sentinel S{i}X " + string.Join(' ', Enumerable.Repeat("filler", 70)))
            .ToList();
        var markdown = string.Join("\n\n", paragraphs);

        var chunks = Chunk(markdown);
        var combined = string.Concat(chunks.Select(c => c.Text));

        for (var i = 0; i < paragraphs.Count; i++)
            Assert.Contains($"S{i}X", combined);
    }
}
