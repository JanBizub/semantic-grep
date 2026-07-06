using System.Text;
using Segrep.Search;

namespace Segrep.InterpreterModel;

public static class ContextFormatter
{
    public static string BuildFlat(IReadOnlyList<SearchResult> chunks)
    {
        var sb = new StringBuilder();
        foreach (var chunk in chunks)
        {
            AppendChunk(sb, chunk);
        }
        return sb.ToString();
    }

    public static string BuildGrouped(IReadOnlyList<SearchResult> chunks)
    {
        var sb = new StringBuilder();
        foreach (var group in chunks.GroupBy(c => Path.GetFileName(c.FilePath)))
        {
            sb.AppendLine($"## Document: {group.Key}");
            sb.AppendLine();
            foreach (var chunk in group)
            {
                AppendChunk(sb, chunk);
            }
        }
        return sb.ToString();
    }

    public static IReadOnlyList<string> DocumentNames(IReadOnlyList<SearchResult> chunks) =>
        chunks.Select(c => Path.GetFileName(c.FilePath)).Distinct().Order().ToList();

    private static void AppendChunk(StringBuilder sb, SearchResult chunk)
    {
        var fileName = Path.GetFileName(chunk.FilePath);
        sb.AppendLine($"[source: {fileName} #{chunk.ChunkIndex}]");
        sb.AppendLine(chunk.ChunkText);
        sb.AppendLine();
    }
}
