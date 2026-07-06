using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Segrep.Configuration;

namespace Segrep.Embeddings;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEmbeddingModel(this IServiceCollection services)
    {
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AzureOpenAIOptions>>().Value;
            return AzureEmbeddingGeneratorFactory.Create(options);
        });

        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<EmbeddingPipeline>();

        return services;
    }
}
