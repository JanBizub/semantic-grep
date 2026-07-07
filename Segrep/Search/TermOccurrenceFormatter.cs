namespace Segrep.Search;

public static class TermOccurrenceFormatter
{
    /// <summary>Formats per-page counts as e.g. "p. 2 (3), p. 5 (2), p. 9 (2)".</summary>
    public static string FormatPages(TermDocumentOccurrences doc)
    {
        var parts = doc.Pages.Select(p => p.Page is int page ? $"p. {page} ({p.Count})" : $"unknown page ({p.Count})");
        var text = string.Join(", ", parts);
        return doc.Approximate ? text + " *" : text;
    }

    /// <summary>One plain-text line per document, e.g. "report.pdf: 7 occurrence(s) — p. 2 (3), p. 5 (2)".</summary>
    public static string BuildSummary(string term, IReadOnlyList<TermDocumentOccurrences> results)
    {
        if (results.Count == 0)
            return $"The term \"{term}\" does not occur in any indexed document.";

        var sb = new System.Text.StringBuilder();
        var total = results.Sum(r => r.TotalCount);
        sb.AppendLine($"Exact occurrences of the term \"{term}\" across the indexed documents ({total} total):");
        foreach (var doc in results)
        {
            var approximate = doc.Approximate ? " (approximate — derived from stored chunks)" : "";
            sb.AppendLine($"- {Path.GetFileName(doc.FilePath)}: {doc.TotalCount} occurrence(s) — {FormatPages(doc with { Approximate = false })}{approximate}");
        }

        return sb.ToString();
    }
}
