using Segrep.DocumentIntelligence;

namespace Segrep.Tests.DocumentIntelligence;

public class PageMapTests
{
    private static PageMap Map(params PageSpan[] spans) =>
        PageMap.FromSpans(spans) ?? throw new InvalidOperationException("expected a non-null map");

    [Fact]
    public void FromSpansReturnsNullWhenEmpty()
    {
        Assert.Null(PageMap.FromSpans([]));
    }

    [Fact]
    public void FromSpansDropsZeroLengthSpans()
    {
        Assert.Null(PageMap.FromSpans([new PageSpan(1, 0, 0)]));

        var map = Map(new PageSpan(1, 0, 0), new PageSpan(2, 10, 5));
        Assert.Equal(2, Assert.Single(map.Spans).PageNumber);
    }

    [Fact]
    public void FromSpansSortsByOffset()
    {
        var map = Map(new PageSpan(2, 100, 50), new PageSpan(1, 0, 50));
        Assert.Equal([1, 2], map.Spans.Select(s => s.PageNumber));
    }

    [Fact]
    public void GetPageReturnsContainingSpan()
    {
        var map = Map(new PageSpan(1, 0, 100), new PageSpan(2, 100, 100), new PageSpan(3, 200, 100));

        Assert.Equal(1, map.GetPage(0));
        Assert.Equal(1, map.GetPage(99));
        Assert.Equal(2, map.GetPage(100));
        Assert.Equal(3, map.GetPage(250));
    }

    [Fact]
    public void OffsetBetweenSpansBelongsToPrecedingPage()
    {
        var map = Map(new PageSpan(1, 0, 50), new PageSpan(2, 100, 50));
        Assert.Equal(1, map.GetPage(75));
    }

    [Fact]
    public void OffsetBeforeFirstSpanReturnsFirstPage()
    {
        var map = Map(new PageSpan(3, 50, 50));
        Assert.Equal(3, map.GetPage(0));
    }

    [Fact]
    public void OffsetPastLastSpanReturnsLastPage()
    {
        var map = Map(new PageSpan(1, 0, 50), new PageSpan(2, 50, 50));
        Assert.Equal(2, map.GetPage(10_000));
    }

    [Fact]
    public void GetPageRangeCoversInclusiveRange()
    {
        var map = Map(new PageSpan(1, 0, 100), new PageSpan(2, 100, 100));

        Assert.Equal((1, 1), map.GetPageRange(0, 100));   // end offset is exclusive
        Assert.Equal((1, 2), map.GetPageRange(50, 150));
        Assert.Equal((2, 2), map.GetPageRange(100, 200));
    }

    [Fact]
    public void GetPageRangeHandlesEmptyRange()
    {
        var map = Map(new PageSpan(1, 0, 100));
        Assert.Equal((1, 1), map.GetPageRange(10, 10));
    }

    [Fact]
    public void JsonRoundTripPreservesSpans()
    {
        var original = Map(new PageSpan(1, 0, 100), new PageSpan(2, 100, 200));

        var restored = PageMap.FromJson(original.ToJson());

        Assert.NotNull(restored);
        Assert.Equal(original.Spans, restored.Spans);
    }

    [Fact]
    public void FromJsonReturnsNullOnInvalidJson()
    {
        Assert.Null(PageMap.FromJson("not json"));
        Assert.Null(PageMap.FromJson("null"));
        Assert.Null(PageMap.FromJson("[]"));
    }
}
