using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Segrep.Documents;

/// <summary>
/// Parses a markdown document into a heading tree. Only top-level headings shape the tree;
/// headings nested inside quotes or lists remain part of their section's body text.
/// </summary>
public sealed class MarkdownDocumentParser
{
    public ParsedDocument Parse(string markdown)
    {
        var ast = Markdown.Parse(markdown);
        var headings = ast.OfType<HeadingBlock>().ToList();
        var errors = Validate(ast, headings);
        if (errors.Count > 0)
        {
            throw new DocumentFormatException(errors);
        }

        var title = headings[0];
        var preamble = SliceBetween(markdown, title, headings.ElementAtOrDefault(1));

        var rootSections = new List<SectionBuilder>();
        var path = new List<SectionBuilder>();
        for (var i = 1; i < headings.Count; i++)
        {
            var heading = headings[i];
            while (path.Count > 0 && path[^1].Level >= heading.Level)
            {
                path.RemoveAt(path.Count - 1);
            }

            var siblings = path.Count == 0 ? rootSections : path[^1].Children;
            var section = new SectionBuilder
            {
                Title = ExtractTitle(heading),
                Content = SliceBetween(markdown, heading, headings.ElementAtOrDefault(i + 1)),
                Level = heading.Level,
                Position = siblings.Count,
            };
            siblings.Add(section);
            path.Add(section);
        }

        return new ParsedDocument(
            ExtractTitle(title),
            preamble,
            rootSections.Select(section => section.ToParsed()).ToList());
    }

    private static List<DocumentFormatError> Validate(MarkdownDocument ast, IReadOnlyList<HeadingBlock> headings)
    {
        var errors = new List<DocumentFormatError>();

        var firstBlock = ast.FirstOrDefault();
        switch (firstBlock)
        {
            case HeadingBlock { Level: 1 }:
                break;
            case HeadingBlock other:
                errors.Add(new(other.Line + 1, $"The document must start with an H1 title, but the first heading is an H{other.Level}."));
                break;
            case null:
                errors.Add(new(1, "The document is empty — it must start with an H1 title."));
                break;
            default:
                errors.Add(new(firstBlock.Line + 1, "Content appears before the H1 document title — the H1 must be the first content in the file."));
                break;
        }

        var previousLevel = 1;
        foreach (var heading in headings.Skip(1))
        {
            if (heading.Level == 1)
            {
                errors.Add(new(heading.Line + 1, "Multiple H1 headings — exactly one H1 (the document title) is allowed."));
                previousLevel = 1;
                continue;
            }

            if (heading.Level > previousLevel + 1)
            {
                errors.Add(new(heading.Line + 1, $"Skipped heading level: H{heading.Level} follows H{previousLevel} — a heading may be at most one level deeper than the previous one."));
            }

            previousLevel = heading.Level;
        }

        foreach (var heading in headings)
        {
            if (string.IsNullOrWhiteSpace(ExtractTitle(heading)))
            {
                errors.Add(new(heading.Line + 1, $"The H{heading.Level} heading has an empty title."));
            }
        }

        return errors.OrderBy(error => error.Line).ToList();
    }

    private static string SliceBetween(string source, HeadingBlock heading, HeadingBlock? next)
    {
        var start = heading.Span.End + 1;
        var end = next?.Span.Start ?? source.Length;
        return start >= end ? string.Empty : source[start..end].Trim();
    }

    private static string ExtractTitle(HeadingBlock heading)
    {
        if (heading.Inline is null)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        AppendInlineText(heading.Inline, text);
        return text.ToString().Trim();
    }

    private static void AppendInlineText(ContainerInline container, StringBuilder text)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    text.Append(literal.Content);
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    break;
                case LineBreakInline:
                    text.Append(' ');
                    break;
                case ContainerInline nested:
                    AppendInlineText(nested, text);
                    break;
            }
        }
    }

    private sealed class SectionBuilder
    {
        public required string Title { get; init; }
        public required string Content { get; init; }
        public required int Level { get; init; }
        public required int Position { get; init; }
        public List<SectionBuilder> Children { get; } = [];

        public ParsedSection ToParsed() =>
            new(Title, Content, Level, Position, Children.Select(child => child.ToParsed()).ToList());
    }
}
