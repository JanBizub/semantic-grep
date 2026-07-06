using System.Text;
using System.Text.RegularExpressions;

namespace Segrep.Chunking;

public sealed class MarkdownChunker
{
    private const int TargetChars = 3200;   // ~800 tokens × 4 chars/token
    private const int OverlapChars = 400;    // ~100 tokens × 4 chars/token

    private static readonly Regex HeadingPattern = new(@"^#{1,6}\s", RegexOptions.Compiled | RegexOptions.Multiline);

    public IReadOnlyList<Chunk> Chunk(string filePath, string fileHash, string markdown)
    {
        var blocks = SplitIntoBlocks(markdown);
        var windows = BuildWindows(blocks);
        return windows
            .Select((text, index) => new Chunk(filePath, fileHash, index, text.Trim()))
            .Where(c => c.Text.Length > 0)
            .ToList();
    }

    private static List<string> SplitIntoBlocks(string markdown)
    {
        // Split at blank lines, keeping heading lines as their own block boundaries.
        var rawBlocks = Regex.Split(markdown, @"\n{2,}");
        var blocks = new List<string>();

        foreach (var block in rawBlocks)
        {
            var trimmed = block.Trim();
            if (trimmed.Length == 0)
                continue;

            // If the block itself contains multiple headings, split further.
            if (HeadingPattern.Matches(trimmed).Count > 1)
            {
                var subBlocks = Regex.Split(trimmed, @"(?=^#{1,6}\s)", RegexOptions.Multiline);
                foreach (var sub in subBlocks)
                {
                    var s = sub.Trim();
                    if (s.Length > 0)
                        blocks.Add(s);
                }
            }
            else
            {
                blocks.Add(trimmed);
            }
        }

        return blocks;
    }

    private static List<string> BuildWindows(List<string> blocks)
    {
        var chunks = new List<string>();
        var buffer = new StringBuilder();
        string? overlap = null;

        foreach (var block in blocks)
        {
            // If adding this block would exceed target and we already have content, flush.
            if (buffer.Length > 0 && buffer.Length + block.Length + 2 > TargetChars)
            {
                chunks.Add(buffer.ToString());
                overlap = TailChars(buffer.ToString(), OverlapChars);
                buffer.Clear();
                if (overlap != null)
                {
                    buffer.Append(overlap);
                    buffer.Append("\n\n");
                }
            }

            if (buffer.Length > 0)
                buffer.Append("\n\n");
            buffer.Append(block);

            // If a single block already exceeds the target, flush it immediately.
            if (buffer.Length >= TargetChars)
            {
                chunks.Add(buffer.ToString());
                overlap = TailChars(buffer.ToString(), OverlapChars);
                buffer.Clear();
                if (overlap != null)
                {
                    buffer.Append(overlap);
                    buffer.Append("\n\n");
                }
            }
        }

        if (buffer.Length > 0)
            chunks.Add(buffer.ToString());

        return chunks;
    }

    // Returns the last `chars` characters of `text` trimmed to a word boundary.
    private static string? TailChars(string text, int chars)
    {
        if (text.Length <= chars)
            return null;
        var tail = text[^chars..];
        // Trim to the first whitespace to avoid cutting mid-word.
        var ws = tail.IndexOfAny([' ', '\n', '\t']);
        return ws > 0 ? tail[ws..].TrimStart() : tail;
    }
}
