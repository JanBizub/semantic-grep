using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Segrep.Configuration;

namespace Segrep.DocumentIntelligence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureDocumentIntelligence(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AzureDocumentIntelligenceOptions>>().Value;
            return new DocumentIntelligenceClient(new Uri(options.Endpoint), new AzureKeyCredential(options.ApiKey));
        });

        services.AddSingleton<DocumentParser>();

        return services;
    }
}
