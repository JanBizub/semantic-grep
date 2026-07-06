namespace Segrep.Search;

public sealed record SearchResult(
    long Id,
    string FilePath,
    string FileHash,
    int ChunkIndex,
    string ChunkText,
    double Score
);
