using Microsoft.Extensions.DependencyInjection;

namespace Segrep.Update;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSelfUpdate(this IServiceCollection services)
    {
        services.AddSingleton<GitHubReleaseClient>();
        services.AddSingleton<SelfUpdater>();
        return services;
    }
}
