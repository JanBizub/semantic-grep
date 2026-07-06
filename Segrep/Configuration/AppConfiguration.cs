using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Segrep.Configuration;

public static class AppConfiguration
{
    public static IConfigurationRoot Build()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")}.json", optional: true, reloadOnChange: false)
            .AddUserSecrets(typeof(AppConfiguration).Assembly, optional: true)
            .AddEnvironmentVariables();

        return builder.Build();
    }

    public static IServiceCollection AddAppConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration);

        services.AddOptions<PostgresOptions>()
            .Bind(configuration.GetSection(PostgresOptions.SectionName));

        services.AddOptions<AzureDocumentIntelligenceOptions>()
            .Bind(configuration.GetSection(AzureDocumentIntelligenceOptions.SectionName));

        services.AddOptions<AzureOpenAIOptions>()
            .Bind(configuration.GetSection(AzureOpenAIOptions.SectionName));

        services.AddOptions<EmbeddingModelOptions>()
            .Bind(configuration.GetSection(EmbeddingModelOptions.SectionName));

        return services;
    }
}
