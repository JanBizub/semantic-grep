using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Segrep.Configuration;

namespace Segrep.Embeddings;

public static class AzureEmbeddingGeneratorFactory
{
    public static IEmbeddingGenerator<string, Embedding<float>> Create(AzureOpenAIOptions options)
    {
        var client = new AzureOpenAIClient(AzureOpenAIOptions.NormalizeEndpoint(options.Endpoint), new AzureKeyCredential(options.ApiKey));
        return client.GetEmbeddingClient(options.EmbeddingDeploymentName).AsIEmbeddingGenerator();
    }
}
