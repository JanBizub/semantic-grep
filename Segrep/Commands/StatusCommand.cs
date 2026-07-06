using Microsoft.Extensions.Options;
using Npgsql;
using Segrep.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class StatusCommand(
    IOptions<PostgresOptions> postgresOptions,
    IOptions<AzureDocumentIntelligenceOptions> documentIntelligenceOptions,
    IOptions<AzureOpenAIOptions> openAiOptions) : AsyncCommand
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var statuses = await AnsiConsole.Status().StartAsync("Checking service status...", _ =>
            Task.WhenAll(
                CheckPostgresAsync(postgresOptions.Value),
                CheckHttpAsync("Azure Document Intelligence", documentIntelligenceOptions.Value.Endpoint, documentIntelligenceOptions.Value.ApiKey),
                CheckHttpAsync("Azure OpenAI", openAiOptions.Value.Endpoint, openAiOptions.Value.ApiKey)));

        var table = new Table().Title("Service Status").Border(TableBorder.Rounded);
        table.AddColumn("Service");
        table.AddColumn("Configured");
        table.AddColumn("Reachable");
        table.AddColumn("Detail");

        foreach (var status in statuses)
        {
            table.AddRow(
                status.Name,
                status.Configured ? "[green]Yes[/]" : "[grey]No[/]",
                status.Configured ? (status.Reachable ? "[green]:check_mark: Reachable[/]" : "[red]:cross_mark: Unreachable[/]") : "[grey]-[/]",
                Markup.Escape(status.Detail));
        }

        AnsiConsole.Write(table);

        return 0;
    }

    private static async Task<ServiceStatus> CheckPostgresAsync(PostgresOptions options)
    {
        const string name = "PostgreSQL";

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new ServiceStatus(name, false, false, "Not configured");
        }

        try
        {
            using var cts = new CancellationTokenSource(Timeout);
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync(cts.Token);
            return new ServiceStatus(name, true, true, $"Connected to {connection.Database}@{connection.Host}:{connection.Port}");
        }
        catch (Exception ex)
        {
            return new ServiceStatus(name, true, false, ex.Message);
        }
    }

    private static async Task<ServiceStatus> CheckHttpAsync(string name, string endpoint, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            return new ServiceStatus(name, false, false, "Not configured");
        }

        try
        {
            using var client = new HttpClient { Timeout = Timeout };
            using var response = await client.GetAsync(endpoint);
            return new ServiceStatus(name, true, true, $"HTTP {(int)response.StatusCode} ({response.StatusCode})");
        }
        catch (Exception ex)
        {
            return new ServiceStatus(name, true, false, ex.Message);
        }
    }

    private sealed record ServiceStatus(string Name, bool Configured, bool Reachable, string Detail);
}
