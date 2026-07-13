using Segrep.Documents;

namespace Segrep.Tests.Documents;

public class MarkdownDocumentParserTests
{
    private readonly MarkdownDocumentParser _parser = new();

    private DocumentFormatException ParseInvalid(string markdown) =>
        Assert.Throws<DocumentFormatException>(() => _parser.Parse(markdown));

    [Fact]
    public void ParsesTitlePreambleAndSectionTree()
    {
        const string markdown =
            """
            # My Document

            Intro paragraph.

            ## Alpha

            Alpha body.

            ### Alpha Child

            Child body.

            ## Beta

            Beta body.
            """;

        var document = _parser.Parse(markdown);

        Assert.Equal("My Document", document.Name);
        Assert.Equal("Intro paragraph.", document.Preamble);
        Assert.Equal(2, document.Sections.Count);
        Assert.Equal(3, document.TotalSectionCount);

        var alpha = document.Sections[0];
        Assert.Equal("Alpha", alpha.Title);
        Assert.Equal("Alpha body.", alpha.Content);
        Assert.Equal(2, alpha.Level);
        Assert.Equal(0, alpha.Position);

        var child = Assert.Single(alpha.Children);
        Assert.Equal("Alpha Child", child.Title);
        Assert.Equal("Child body.", child.Content);
        Assert.Equal(3, child.Level);
        Assert.Equal(0, child.Position);

        var beta = document.Sections[1];
        Assert.Equal("Beta", beta.Title);
        Assert.Equal(1, beta.Position);
        Assert.Empty(beta.Children);
    }

    [Fact]
    public void TitleOnlyDocumentIsValid()
    {
        var document = _parser.Parse("# Just a Title");

        Assert.Equal("Just a Title", document.Name);
        Assert.Equal(string.Empty, document.Preamble);
        Assert.Empty(document.Sections);
    }

    [Fact]
    public void SiblingAfterDeeperNestingAttachesToCorrectParent()
    {
        const string markdown =
            """
            # Doc

            ## A

            ### A1

            #### A1a

            ## B
            """;

        var document = _parser.Parse(markdown);

        Assert.Equal(2, document.Sections.Count);
        Assert.Equal("B", document.Sections[1].Title);
        Assert.Equal("A1a", document.Sections[0].Children[0].Children[0].Title);
    }

    [Fact]
    public void DuplicateSiblingTitlesAreAllowed()
    {
        const string markdown =
            """
            # Doc

            ## Notes

            ## Notes
            """;

        var document = _parser.Parse(markdown);

        Assert.Equal(2, document.Sections.Count);
        Assert.All(document.Sections, s => Assert.Equal("Notes", s.Title));
        Assert.Equal([0, 1], document.Sections.Select(s => s.Position));
    }

    [Fact]
    public void HeadingInsideCodeFenceStaysInBody()
    {
        const string markdown =
            """
            # Doc

            ## Section

            ```
            ## not a heading
            ```
            """;

        var document = _parser.Parse(markdown);

        var section = Assert.Single(document.Sections);
        Assert.Contains("## not a heading", section.Content);
        Assert.Empty(section.Children);
    }

    [Fact]
    public void InlineFormattingIsStrippedFromTitles()
    {
        var document = _parser.Parse("# The **Bold** `code` Title");
        Assert.Equal("The Bold code Title", document.Name);
    }

    [Fact]
    public void EmptyDocumentFails()
    {
        var ex = ParseInvalid("");

        var error = Assert.Single(ex.Errors);
        Assert.Equal(1, error.Line);
        Assert.Contains("empty", error.Message);
    }

    [Fact]
    public void ContentBeforeH1Fails()
    {
        var ex = ParseInvalid("some preamble text\n\n# Title");

        Assert.Contains(ex.Errors, e => e.Line == 1 && e.Message.Contains("before the H1"));
    }

    [Fact]
    public void FirstHeadingNotH1Fails()
    {
        var ex = ParseInvalid("## Not a Title");

        Assert.Contains(ex.Errors, e => e.Line == 1 && e.Message.Contains("H2"));
    }

    [Fact]
    public void MultipleH1Fails()
    {
        var ex = ParseInvalid("# One\n\n# Two");

        Assert.Contains(ex.Errors, e => e.Line == 3 && e.Message.Contains("Multiple H1"));
    }

    [Fact]
    public void SkippedHeadingLevelFails()
    {
        var ex = ParseInvalid("# Title\n\n### Skipped");

        Assert.Contains(ex.Errors, e => e.Line == 3 && e.Message.Contains("Skipped heading level"));
    }

    [Fact]
    public void HeadingOneLevelShallowerThanPreviousIsValid()
    {
        const string markdown =
            """
            # Doc

            ## A

            ### A1

            ## B
            """;

        var document = _parser.Parse(markdown);
        Assert.Equal(2, document.Sections.Count);
    }

    [Fact]
    public void EmptyHeadingTitleFails()
    {
        var ex = ParseInvalid("# Title\n\n##   ");

        Assert.Contains(ex.Errors, e => e.Message.Contains("empty title"));
    }

    [Fact]
    public void MultipleErrorsAreCollectedAndOrderedByLine()
    {
        const string markdown =
            """
            ## Wrong Level

            #### Skipped

            # Late Title
            """;

        var ex = ParseInvalid(markdown);

        Assert.True(ex.Errors.Count >= 2);
        Assert.Equal(ex.Errors.OrderBy(e => e.Line).Select(e => e.Line), ex.Errors.Select(e => e.Line));
    }
}
