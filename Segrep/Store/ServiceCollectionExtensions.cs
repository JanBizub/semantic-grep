using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Segrep.Configuration;

namespace Segrep.Store;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresStore(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PostgresOptions>>().Value;
            return SegrepDataSource.Create(options);
        });

        services.AddSingleton<DocumentStore>();

        return services;
    }
}
