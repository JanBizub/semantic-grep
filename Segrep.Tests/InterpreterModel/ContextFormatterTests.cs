using Segrep.InterpreterModel;
using Segrep.Search;

namespace Segrep.Tests.InterpreterModel;

public class ContextFormatterTests
{
    private static SearchResult Result(
        long id, string filePath, int chunkIndex = 0, string text = "text",
        int? pageStart = null, int? pageEnd = null) =>
        new(id, filePath, "hash", chunkIndex, text, 1.0, pageStart, pageEnd);

    [Fact]
    public void BuildFlatEmitsSourceTagAndText()
    {
        var context = ContextFormatter.BuildFlat([Result(1, "/docs/report.pdf", 3, "the content")]);

        Assert.Contains("[source: report.pdf #3]", context);
        Assert.Contains("the content", context);
    }

    [Fact]
    public void SinglePageIsFormattedAsP()
    {
        var context = ContextFormatter.BuildFlat([Result(1, "/a.pdf", 0, "t", pageStart: 5, pageEnd: 5)]);
        Assert.Contains("[source: a.pdf #0, p. 5]", context);
    }

    [Fact]
    public void PageRangeIsFormattedAsPp()
    {
        var context = ContextFormatter.BuildFlat([Result(1, "/a.pdf", 0, "t", pageStart: 5, pageEnd: 7)]);
        Assert.Contains("[source: a.pdf #0, pp. 5-7]", context);
    }

    [Fact]
    public void MissingPageEndFallsBackToPageStart()
    {
        var context = ContextFormatter.BuildFlat([Result(1, "/a.pdf", 0, "t", pageStart: 5)]);
        Assert.Contains("[source: a.pdf #0, p. 5]", context);
    }

    [Fact]
    public void BuildGroupedGroupsChunksUnderDocumentHeadings()
    {
        var context = ContextFormatter.BuildGrouped(
        [
            Result(1, "/docs/a.pdf", 0, "a zero"),
            Result(2, "/docs/a.pdf", 1, "a one"),
            Result(3, "/docs/b.pdf", 0, "b zero"),
        ]);

        var aHeading = context.IndexOf("## Document: a.pdf", StringComparison.Ordinal);
        var bHeading = context.IndexOf("## Document: b.pdf", StringComparison.Ordinal);
        Assert.True(aHeading >= 0 && bHeading > aHeading);
        Assert.True(context.IndexOf("a one", StringComparison.Ordinal) < bHeading);
    }

    [Fact]
    public void DocumentNamesAreDistinctAndSorted()
    {
        var names = ContextFormatter.DocumentNames(
        [
            Result(1, "/x/b.pdf"),
            Result(2, "/y/a.pdf"),
            Result(3, "/z/b.pdf"),
        ]);

        Assert.Equal(["a.pdf", "b.pdf"], names);
    }

    [Fact]
    public void EmptyInputProducesEmptyOutput()
    {
        Assert.Equal(string.Empty, ContextFormatter.BuildFlat([]));
        Assert.Equal(string.Empty, ContextFormatter.BuildGrouped([]));
        Assert.Empty(ContextFormatter.DocumentNames([]));
    }
}
