using Microsoft.Extensions.DependencyInjection;

namespace Segrep.Documents;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocuments(this IServiceCollection services)
    {
        services.AddSingleton<MarkdownDocumentParser>();

        return services;
    }
}
