using System.Text;
using System.Text.RegularExpressions;
using Segrep.DocumentIntelligence;

namespace Segrep.Chunking;

public sealed class MarkdownChunker
{
    private const int TargetChars = 3200;   // ~800 tokens × 4 chars/token
    private const int OverlapChars = 400;    // ~100 tokens × 4 chars/token

    private static readonly Regex HeadingPattern = new(@"^#{1,6}\s", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex BlankLinePattern = new(@"\n{2,}", RegexOptions.Compiled);

    // Start/End are character offsets into the original markdown, so chunks can be
    // attributed to source pages via PageMap even though the window text itself is
    // rebuilt with normalized separators.
    private readonly record struct Block(string Text, int Start, int End);
    private readonly record struct Window(string Text, int Start, int End);

    public IReadOnlyList<Chunk> Chunk(string filePath, string fileHash, string markdown, PageMap? pages = null)
    {
        var blocks = SplitIntoBlocks(markdown);
        var windows = BuildWindows(blocks);
        return windows
            .Select((window, index) =>
            {
                int? pageStart = null, pageEnd = null;
                if (pages is not null)
                    (pageStart, pageEnd) = pages.GetPageRange(window.Start, window.End);
                return new Chunk(filePath, fileHash, index, window.Text.Trim(), pageStart, pageEnd);
            })
            .Where(c => c.Text.Length > 0)
            .ToList();
    }

    private static List<Block> SplitIntoBlocks(string markdown)
    {
        // Split at blank lines, keeping heading lines as their own block boundaries.
        var blocks = new List<Block>();

        void AddTrimmed(int start, int end)
        {
            TrimRange(markdown, ref start, ref end);
            if (start < end)
                blocks.Add(new Block(markdown[start..end], start, end));
        }

        void AddRaw(int start, int end)
        {
            TrimRange(markdown, ref start, ref end);
            if (start >= end)
                return;

            var text = markdown[start..end];

            // If the block itself contains multiple headings, split further.
            var headings = HeadingPattern.Matches(text);
            if (headings.Count > 1)
            {
                var subStart = 0;
                foreach (var boundary in headings.Select(m => m.Index).Where(i => i > 0))
                {
                    AddTrimmed(start + subStart, start + boundary);
                    subStart = boundary;
                }
                AddTrimmed(start + subStart, end);
            }
            else
            {
                blocks.Add(new Block(text, start, end));
            }
        }

        var position = 0;
        foreach (Match separator in BlankLinePattern.Matches(markdown))
        {
            AddRaw(position, separator.Index);
            position = separator.Index + separator.Length;
        }
        AddRaw(position, markdown.Length);

        return blocks;
    }

    private static void TrimRange(string text, ref int start, ref int end)
    {
        while (start < end && char.IsWhiteSpace(text[start]))
            start++;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;
    }

    private static List<Window> BuildWindows(List<Block> blocks)
    {
        const int Unset = -1;
        var windows = new List<Window>();
        var buffer = new StringBuilder();
        var start = Unset;  // original-markdown offset where the current window begins
        var end = 0;        // original-markdown offset where the current window ends

        void Flush()
        {
            var text = buffer.ToString();
            windows.Add(new Window(text, Math.Max(start, 0), end));
            buffer.Clear();
            var overlap = TailChars(text, OverlapChars);
            if (overlap != null)
            {
                buffer.Append(overlap);
                buffer.Append("\n\n");
                // The overlap repeats the tail of the flushed window, so the next
                // window starts (approximately) that many characters before its end.
                start = Math.Max(Math.Max(start, 0), end - overlap.Length);
            }
            else
            {
                start = Unset;
            }
        }

        foreach (var block in blocks)
        {
            // If adding this block would exceed target and we already have content, flush.
            if (buffer.Length > 0 && buffer.Length + block.Text.Length + 2 > TargetChars)
                Flush();

            if (buffer.Length > 0)
                buffer.Append("\n\n");
            if (start == Unset)
                start = block.Start;
            buffer.Append(block.Text);
            end = block.End;

            // If a single block already exceeds the target, flush it immediately.
            if (buffer.Length >= TargetChars)
                Flush();
        }

        if (buffer.Length > 0)
            windows.Add(new Window(buffer.ToString(), Math.Max(start, 0), end));

        return windows;
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
