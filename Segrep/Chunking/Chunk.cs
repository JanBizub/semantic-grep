namespace Segrep.Chunking;

public sealed record Chunk(string FilePath, string FileHash, int ChunkIndex, string Text);
