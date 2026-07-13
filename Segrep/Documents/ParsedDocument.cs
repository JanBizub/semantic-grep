namespace Segrep.Documents;

public sealed record ParsedDocument(string Name, string Preamble, IReadOnlyList<ParsedSection> Sections)
{
    public int TotalSectionCount => CountSections(Sections);

    private static int CountSections(IReadOnlyList<ParsedSection> sections) =>
        sections.Sum(section => 1 + CountSections(section.Children));
}

public sealed record ParsedSection(
    string Title,
    string Content,
    int Level,
    int Position,
    IReadOnlyList<ParsedSection> Children);
