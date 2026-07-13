using Segrep.Search;

namespace Segrep.Tests.Search;

public class TermOccurrenceFormatterTests
{
    [Fact]
    public void FormatPagesListsPerPageCounts()
    {
        var doc = new TermDocumentOccurrences("/docs/report.pdf", 7,
            [new PageOccurrences(2, 3), new PageOccurrences(5, 2), new PageOccurrences(9, 2)],
            Approximate: false);

        Assert.Equal("p. 2 (3), p. 5 (2), p. 9 (2)", TermOccurrenceFormatter.FormatPages(doc));
    }

    [Fact]
    public void FormatPagesRendersUnknownPage()
    {
        var doc = new TermDocumentOccurrences("/a.pdf", 4,
            [new PageOccurrences(1, 3), new PageOccurrences(null, 1)],
            Approximate: false);

        Assert.Equal("p. 1 (3), unknown page (1)", TermOccurrenceFormatter.FormatPages(doc));
    }

    [Fact]
    public void FormatPagesMarksApproximateWithAsterisk()
    {
        var doc = new TermDocumentOccurrences("/a.pdf", 1, [new PageOccurrences(1, 1)], Approximate: true);

        Assert.EndsWith(" *", TermOccurrenceFormatter.FormatPages(doc));
    }

    [Fact]
    public void BuildSummaryReportsNoOccurrences()
    {
        var summary = TermOccurrenceFormatter.BuildSummary("Keter", []);

        Assert.Equal("The term \"Keter\" does not occur in any indexed document.", summary);
    }

    [Fact]
    public void BuildSummaryListsEachDocumentWithTotal()
    {
        var results = new List<TermDocumentOccurrences>
        {
            new("/docs/report.pdf", 5, [new PageOccurrences(2, 3), new PageOccurrences(5, 2)], Approximate: false),
            new("/docs/notes.pdf", 2, [new PageOccurrences(1, 2)], Approximate: false),
        };

        var summary = TermOccurrenceFormatter.BuildSummary("Keter", results);

        Assert.Contains("\"Keter\"", summary);
        Assert.Contains("(7 total)", summary);
        Assert.Contains("- report.pdf: 5 occurrence(s) — p. 2 (3), p. 5 (2)", summary);
        Assert.Contains("- notes.pdf: 2 occurrence(s) — p. 1 (2)", summary);
    }

    [Fact]
    public void BuildSummaryNotesApproximateDocumentsInWords()
    {
        var results = new List<TermDocumentOccurrences>
        {
            new("/docs/a.pdf", 1, [new PageOccurrences(1, 1)], Approximate: true),
        };

        var summary = TermOccurrenceFormatter.BuildSummary("x", results);

        Assert.Contains("(approximate — derived from stored chunks)", summary);
        // The asterisk shorthand is for tables; the summary spells it out instead.
        Assert.DoesNotContain("p. 1 (1) *", summary);
    }
}
