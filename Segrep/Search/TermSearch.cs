using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Npgsql;
using Segrep.Configuration;
using Segrep.DocumentIntelligence;

namespace Segrep.Search;

/// <summary>Occurrences of a term on one page; <c>Page</c> is null when no page info is available.</summary>
public sealed record PageOccurrences(int? Page, int Count);

/// <summary>
/// All occurrences of a term in one document. <c>Approximate</c> is true when the parse cache
/// was unavailable and counts/pages were derived from stored chunks instead of the full Markdown.
/// </summary>
public sealed record TermDocumentOccurrences(
    string FilePath,
    int TotalCount,
    IReadOnlyList<PageOccurrences> Pages,
    bool Approximate
);

/// <summary>
/// Counts literal (word-boundary, case-insensitive) occurrences of a term across indexed
/// documents and locates the pages they appear on. Unlike HybridSearch this is exhaustive:
/// it scans the full document Markdown from the parse cache, not just top-ranked chunks,
/// so occurrence counts are exact. Falls back to overlap-deduplicated chunk scanning when
/// the cache file for a document is missing.
/// </summary>
public sealed class TermSearch(NpgsqlDataSource dataSource, IOptions<AzureDocumentIntelligenceOptions> diOptions)
{
    // Must stay >= MarkdownChunker.OverlapChars: bounds the suffix/prefix probe that
    // detects duplicated overlap text between adjacent chunks in the fallback path.
    private const int MaxChunkOverlap = 400;

    public async Task<IReadOnlyList<TermDocumentOccurrences>> FindAsync(
        string term,
        string? documentFilter = null,
        CancellationToken cancellationToken = default)
    {
        term = term.Trim();
        if (term.Length == 0)
            return [];

        var regex = BuildTermRegex(term);
        var results = new List<TermDocumentOccurrences>();

        foreach (var (filePath, fileHash) in await FindCandidateDocumentsAsync(term, documentFilter, cancellationToken))
        {
            var occurrences =
                await CountFromCacheAsync(regex, filePath, fileHash, cancellationToken)
                ?? await CountFromChunksAsync(regex, filePath, fileHash, cancellationToken);

            if (occurrences.TotalCount > 0)
                results.Add(occurrences);
        }

        return results.OrderByDescending(r => r.TotalCount).ThenBy(r => r.FilePath).ToList();
    }

    // Literal match at word boundaries; lookarounds instead of \b so terms that start or
    // end with non-word characters (e.g. "C#") still match.
    private static Regex BuildTermRegex(string term) =>
        new($@"(?<!\w){Regex.Escape(term)}(?!\w)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Cheap DB prefilter: documents whose chunks contain the term as a substring
    /// (superset of word-boundary matches; served by the trigram index). Deduplicated
    /// by content hash — the same file indexed under several paths is one document.
    /// </summary>
    private async Task<IReadOnlyList<(string FilePath, string FileHash)>> FindCandidateDocumentsAsync(
        string term,
        string? documentFilter,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT min(file_path), file_hash
            FROM ai_doc_chunk
            WHERE chunk_text ILIKE $1
              AND ($2::text IS NULL OR file_name ILIKE '%' || $2 || '%')
            GROUP BY file_hash
            ORDER BY min(file_path)
            """;

        var pattern = "%" + term
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_") + "%";

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(pattern);
        command.Parameters.Add(new NpgsqlParameter<string?> { TypedValue = documentFilter });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var candidates = new List<(string, string)>();
        while (await reader.ReadAsync(cancellationToken))
            candidates.Add((reader.GetString(0), reader.GetString(1)));

        return candidates;
    }

    /// <summary>Exact path: scan the full cached Markdown and map match offsets to pages.</summary>
    private async Task<TermDocumentOccurrences?> CountFromCacheAsync(
        Regex regex,
        string filePath,
        string fileHash,
        CancellationToken cancellationToken)
    {
        var cachePath = diOptions.Value.CachePath;
        var markdownFile = Path.Combine(cachePath, $"{fileHash}.md");
        if (!File.Exists(markdownFile))
            return null;

        var markdown = await File.ReadAllTextAsync(markdownFile, cancellationToken);

        var pagesFile = Path.Combine(cachePath, $"{fileHash}.pages.json");
        var pages = File.Exists(pagesFile)
            ? PageMap.FromJson(await File.ReadAllTextAsync(pagesFile, cancellationToken))
            : null;

        var counts = new Dictionary<int, int>();
        var unknown = 0;
        var total = 0;
        foreach (Match match in regex.Matches(markdown))
        {
            total++;
            if (pages?.GetPage(match.Index) is int page)
                counts[page] = counts.GetValueOrDefault(page) + 1;
            else
                unknown++;
        }

        return new TermDocumentOccurrences(filePath, total, OrderPages(counts, unknown), Approximate: false);
    }

    /// <summary>
    /// Fallback when the parse cache is gone: scan stored chunks, skipping matches that fall
    /// inside the text a chunk repeats from its predecessor (the chunker's overlap window),
    /// so duplicated text is not double-counted. Pages come from each chunk's page range,
    /// so they are attributed to the chunk's first page and marked approximate.
    /// </summary>
    private async Task<TermDocumentOccurrences> CountFromChunksAsync(
        Regex regex,
        string filePath,
        string fileHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT ON (chunk_index) chunk_index, chunk_text, page_start
            FROM ai_doc_chunk
            WHERE file_path = $1 AND file_hash = $2
            ORDER BY chunk_index
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(filePath);
        command.Parameters.AddWithValue(fileHash);

        var chunks = new List<(string Text, int? PageStart)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                chunks.Add((reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2)));
        }

        var counts = new Dictionary<int, int>();
        var unknown = 0;
        var total = 0;
        string? previousText = null;
        foreach (var (text, pageStart) in chunks)
        {
            var overlapLength = previousText is null ? 0 : SharedOverlapLength(previousText, text);
            foreach (Match match in regex.Matches(text))
            {
                if (match.Index < overlapLength)
                    continue; // already counted in the previous chunk
                total++;
                if (pageStart is int page)
                    counts[page] = counts.GetValueOrDefault(page) + 1;
                else
                    unknown++;
            }

            previousText = text;
        }

        return new TermDocumentOccurrences(filePath, total, OrderPages(counts, unknown), Approximate: true);
    }

    // Length of the longest suffix of `previous` that is a prefix of `current`,
    // bounded by the chunker's overlap size — the text duplicated between the two.
    private static int SharedOverlapLength(string previous, string current)
    {
        var max = Math.Min(MaxChunkOverlap, Math.Min(previous.Length, current.Length));
        for (var length = max; length > 0; length--)
        {
            if (string.CompareOrdinal(previous, previous.Length - length, current, 0, length) == 0)
                return length;
        }

        return 0;
    }

    private static List<PageOccurrences> OrderPages(Dictionary<int, int> counts, int unknownCount)
    {
        var pages = counts
            .OrderBy(kv => kv.Key)
            .Select(kv => new PageOccurrences(kv.Key, kv.Value))
            .ToList();
        if (unknownCount > 0)
            pages.Add(new PageOccurrences(null, unknownCount));
        return pages;
    }
}
