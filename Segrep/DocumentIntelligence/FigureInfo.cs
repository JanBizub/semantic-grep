namespace Segrep.DocumentIntelligence;

/// <summary>
/// A figure detected by Azure Document Intelligence: its id (used to name the cached
/// cropped image), its character span in the parsed Markdown, and its source page.
/// </summary>
public sealed record FigureInfo(string Id, int Offset, int Length, int? PageNumber);
