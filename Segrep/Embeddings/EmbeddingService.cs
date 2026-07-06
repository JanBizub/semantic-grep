using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Segrep.Configuration;

namespace Segrep.Embeddings;

public sealed class EmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly EmbeddingModelOptions _options;

    public EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> generator, IOptions<EmbeddingModelOptions> options)
    {
        _generator = generator;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<Embedding<float>>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        var generationOptions = new EmbeddingGenerationOptions
        {
            ModelId = _options.ModelName,
            Dimensions = _options.Dimensions,
        };

        var results = new List<Embedding<float>>(texts.Count);
        foreach (var batch in texts.Chunk(_options.BatchSize))
        {
            var embeddings = await _generator.GenerateAsync(batch, generationOptions, cancellationToken);
            results.AddRange(embeddings);
        }

        return results;
    }
}
