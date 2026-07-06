namespace Segrep.Configuration;

public sealed class EmbeddingModelOptions
{
    public const string SectionName = "EmbeddingModel";

    public string ModelName { get; set; } = "text-embedding-3-large";

    public int Dimensions { get; set; } = 1536;

    public int BatchSize { get; set; } = 100;
}
