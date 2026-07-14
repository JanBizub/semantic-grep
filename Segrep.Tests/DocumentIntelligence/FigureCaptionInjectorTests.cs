using Segrep.Chunking;
using Segrep.DocumentIntelligence;

namespace Segrep.Tests.DocumentIntelligence;

public class FigureCaptionInjectorTests
{
    [Fact]
    public void Inject_InsertsCaptionBeforeClosingTag()
    {
        var markdown = "Intro\n\n<figure>\n\n#1 TOP\n\n</figure>\n\nOutro";
        var figure = new FigureInfo("1.1", markdown.IndexOf("<figure>"), "<figure>\n\n#1 TOP\n\n</figure>".Length, 1);

        var (result, _) = FigureCaptionInjector.Inject(markdown, null, [new FigureCaption(figure, "A badge.")]);

        Assert.Contains("**Image description:** A badge.\n</figure>", result);
        Assert.StartsWith("Intro", result);
        Assert.EndsWith("Outro", result);
    }

    [Fact]
    public void Inject_WithoutClosingTagInSpan_InsertsAtSpanEnd()
    {
        var markdown = "Some figure content here. Trailing text.";
        var figure = new FigureInfo("1", 0, "Some figure content here.".Length, 1);

        var (result, _) = FigureCaptionInjector.Inject(markdown, null, [new FigureCaption(figure, "A photo.")]);

        Assert.Equal("Some figure content here.\n\n**Image description:** A photo.\n Trailing text.", result);
    }

    [Fact]
    public void Inject_AppendsStandaloneImageCaptionAtEnd()
    {
        var markdown = "OCR text from an image.";
        var figure = new FigureInfo("file", markdown.Length, 0, 1);

        var (result, _) = FigureCaptionInjector.Inject(markdown, null, [new FigureCaption(figure, "A cat.")]);

        Assert.Equal("OCR text from an image.\n\n**Image description:** A cat.\n", result);
    }

    [Fact]
    public void Inject_MultipleFigures_AllCaptionsLandInsideTheirFigures()
    {
        var markdown = "<figure>one</figure>\n\nmiddle\n\n<figure>two</figure>";
        var first = new FigureInfo("1", 0, "<figure>one</figure>".Length, 1);
        var second = new FigureInfo("2", markdown.IndexOf("<figure>two"), "<figure>two</figure>".Length, 2);

        var (result, _) = FigureCaptionInjector.Inject(markdown, null,
            [new FigureCaption(first, "First."), new FigureCaption(second, "Second.")]);

        Assert.Contains("one\n\n**Image description:** First.\n</figure>", result);
        Assert.Contains("two\n\n**Image description:** Second.\n</figure>", result);
        var middleIndex = result.IndexOf("middle");
        Assert.True(result.IndexOf("First.") < middleIndex);
        Assert.True(result.IndexOf("Second.") > middleIndex);
    }

    [Fact]
    public void Inject_SkipsBlankCaptions()
    {
        var markdown = "<figure>x</figure>";
        var figure = new FigureInfo("1", 0, markdown.Length, 1);

        var (result, pages) = FigureCaptionInjector.Inject(markdown, null, [new FigureCaption(figure, "   ")]);

        Assert.Equal(markdown, result);
        Assert.Null(pages);
    }

    [Fact]
    public void Inject_ExtendsContainingPageSpanAndShiftsLaterSpans()
    {
        // Page 1: [0, 40), page 2: [40, 80). Figure sits inside page 1.
        var markdown = new string('a', 10) + "<figure>fig</figure>" + new string('b', 10) + new string('c', 40);
        var pages = PageMap.FromSpans([new PageSpan(1, 0, 40), new PageSpan(2, 40, 40)]);
        var figure = new FigureInfo("1", 10, "<figure>fig</figure>".Length, 1);
        var caption = new FigureCaption(figure, "A chart.");

        var (result, adjusted) = FigureCaptionInjector.Inject(markdown, pages, [caption]);

        Assert.NotNull(adjusted);
        var inserted = FigureCaptionInjector.FormatCaption("A chart.").Length;

        // The caption itself is attributed to page 1.
        var captionIndex = result.IndexOf("**Image description:**");
        Assert.Equal(1, adjusted.GetPage(captionIndex));

        // Content that was on page 2 is still attributed to page 2 at its shifted offset.
        var firstC = result.IndexOf("cccc");
        Assert.Equal(40 + inserted, firstC);
        Assert.Equal(2, adjusted.GetPage(firstC));
        Assert.Equal((2, 2), adjusted.GetPageRange(firstC, result.Length));
    }

    [Fact]
    public void Inject_EnrichedMarkdownFlowsThroughChunkerWithCorrectPages()
    {
        var markdown = "# Doc\n\n<figure>\n\nchart labels\n\n</figure>\n\nBody text on page two.";
        var figureStart = markdown.IndexOf("<figure>");
        var figureLength = markdown.IndexOf("</figure>") + "</figure>".Length - figureStart;
        var pages = PageMap.FromSpans([
            new PageSpan(1, 0, markdown.IndexOf("Body")),
            new PageSpan(2, markdown.IndexOf("Body"), markdown.Length - markdown.IndexOf("Body")),
        ]);
        var caption = new FigureCaption(new FigureInfo("1", figureStart, figureLength, 1), "Revenue trends upward.");

        var (enriched, adjusted) = FigureCaptionInjector.Inject(markdown, pages, [caption]);
        var chunks = new MarkdownChunker().Chunk("doc.pdf", "hash", enriched, adjusted);

        var captionChunk = Assert.Single(chunks, c => c.Text.Contains("Revenue trends upward."));
        Assert.Equal(1, captionChunk.PageStart);
        Assert.Contains(chunks, c => c.Text.Contains("Body text on page two."));
    }
}
