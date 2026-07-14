using System.Text;

namespace Segrep.DocumentIntelligence;

/// <summary>A vision-model description for one figure, ready to be injected into the Markdown.</summary>
public sealed record FigureCaption(FigureInfo Figure, string Caption);

/// <summary>
/// Inserts figure captions into the parsed Markdown (just before each figure's closing tag)
/// and shifts the <see cref="PageMap"/> spans so page attribution stays correct downstream.
/// </summary>
internal static class FigureCaptionInjector
{
    private const string ClosingTag = "</figure>";

    public static (string Markdown, PageMap? Pages) Inject(
        string markdown,
        PageMap? pages,
        IReadOnlyList<FigureCaption> captions)
    {
        var insertions = captions
            .Where(c => !string.IsNullOrWhiteSpace(c.Caption))
            .Select(c => (Index: FindInsertionIndex(markdown, c.Figure), Text: FormatCaption(c.Caption)))
            .OrderByDescending(i => i.Index)
            .ToList();

        if (insertions.Count == 0)
            return (markdown, pages);

        var builder = new StringBuilder(markdown);
        var spans = pages?.Spans.ToList();

        // Applied back-to-front so earlier insertion indexes stay valid.
        foreach (var (index, text) in insertions)
        {
            builder.Insert(index, text);
            if (spans is null)
                continue;

            for (var i = 0; i < spans.Count; i++)
            {
                var span = spans[i];
                if (span.Offset > index)
                    spans[i] = span with { Offset = span.Offset + text.Length };
                else if (span.Offset + span.Length > index)
                    spans[i] = span with { Length = span.Length + text.Length };
                // Insertions in the gap after a span need no adjustment: PageMap already
                // attributes between-span content to the preceding page.
            }
        }

        var adjusted = spans is null ? null : PageMap.FromSpans(spans);
        return (builder.ToString(), adjusted);
    }

    internal static string FormatCaption(string caption) => $"\n\n**Image description:** {caption.Trim()}\n";

    /// <summary>
    /// Insertion point for a figure's caption: just before the figure's closing tag if the
    /// span contains one, otherwise at the end of the span.
    /// </summary>
    internal static int FindInsertionIndex(string markdown, FigureInfo figure)
    {
        var start = Math.Clamp(figure.Offset, 0, markdown.Length);
        var end = Math.Clamp(figure.Offset + figure.Length, start, markdown.Length);

        var closing = markdown.AsSpan(start, end - start).LastIndexOf(ClosingTag);
        return closing >= 0 ? start + closing : end;
    }
}
