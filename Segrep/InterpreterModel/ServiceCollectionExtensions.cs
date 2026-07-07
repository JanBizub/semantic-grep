using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Segrep.Configuration;

namespace Segrep.InterpreterModel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInterpreterModel(this IServiceCollection services)
    {
        services.AddSingleton<IChatClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AzureOpenAIOptions>>().Value;
            var client = new AzureOpenAIClient(AzureOpenAIOptions.NormalizeEndpoint(options.Endpoint), new AzureKeyCredential(options.ApiKey));
            return client.GetChatClient(options.ChatDeploymentName).AsIChatClient();
        });

        services.AddSingleton<SubTaskExecutor>();

        return services;
    }
}
