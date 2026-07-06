namespace Segrep.Configuration;

public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    public string Endpoint { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string EmbeddingDeploymentName { get; set; } = string.Empty;

    public string ChatDeploymentName { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey);

    // Returns scheme://host[:port]/ — drops any request path (e.g. the "/openai/v1/responses"
    // that the Azure AI Foundry portal includes in its target URI). AzureOpenAIClient expects the
    // bare resource endpoint and appends the deployment/operation path itself.
    public static Uri NormalizeEndpoint(string endpoint)
    {
        var uri = new Uri(endpoint);
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }
}
