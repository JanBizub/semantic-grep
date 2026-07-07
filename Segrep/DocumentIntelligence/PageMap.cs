using System.Text.Json;

namespace Segrep.DocumentIntelligence;

/// <summary>A page's span within the parsed Markdown content (offset/length in characters).</summary>
public sealed record PageSpan(int PageNumber, int Offset, int Length);

/// <summary>
/// Maps character offsets in the Markdown produced by Azure Document Intelligence back to
/// source-document page numbers, using the per-page content spans from the AnalyzeResult.
/// </summary>
public sealed class PageMap
{
    private readonly PageSpan[] _spans;

    private PageMap(PageSpan[] spans) => _spans = spans;

    public IReadOnlyList<PageSpan> Spans => _spans;

    public static PageMap? FromSpans(IEnumerable<PageSpan> spans)
    {
        var sorted = spans.Where(s => s.Length > 0).OrderBy(s => s.Offset).ToArray();
        return sorted.Length == 0 ? null : new PageMap(sorted);
    }

    /// <summary>
    /// Best-effort page for a character offset: the page whose span contains it, or the
    /// nearest span starting before it (content between spans belongs to the preceding page).
    /// </summary>
    public int GetPage(int offset)
    {
        var lo = 0;
        var hi = _spans.Length - 1;
        var best = 0;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_spans[mid].Offset <= offset)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return _spans[best].PageNumber;
    }

    /// <summary>Inclusive page range covered by the character range [startOffset, endOffset).</summary>
    public (int PageStart, int PageEnd) GetPageRange(int startOffset, int endOffset)
    {
        var start = GetPage(startOffset);
        var end = GetPage(Math.Max(startOffset, endOffset - 1));
        return start <= end ? (start, end) : (end, start);
    }

    public string ToJson() => JsonSerializer.Serialize(_spans);

    public static PageMap? FromJson(string json)
    {
        try
        {
            var spans = JsonSerializer.Deserialize<PageSpan[]>(json);
            return spans is null ? null : FromSpans(spans);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
