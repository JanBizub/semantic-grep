using Microsoft.Extensions.Options;
using Segrep.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class ConfigureCommand(
    IOptions<PostgresOptions> postgresOptions,
    IOptions<AzureDocumentIntelligenceOptions> documentIntelligenceOptions,
    IOptions<AzureOpenAIOptions> openAiOptions) : Command
{
    private enum Section { Postgres, AzureDocumentIntelligence, AzureOpenAI, Done }

    protected override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        AnsiConsole.Write(new Rule("[bold]Configure Services[/]").RuleStyle("grey"));

        var updates = new Dictionary<string, IDictionary<string, string?>>();

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<Section>()
                    .Title("Select a section to configure:")
                    .UseConverter(s => s switch
                    {
                        Section.Postgres => "PostgreSQL",
                        Section.AzureDocumentIntelligence => "Azure Document Intelligence",
                        Section.AzureOpenAI => "Azure OpenAI",
                        Section.Done => "Done",
                        _ => s.ToString()
                    })
                    .AddChoices(Section.Postgres, Section.AzureDocumentIntelligence, Section.AzureOpenAI, Section.Done));

            if (choice == Section.Done)
                break;

            switch (choice)
            {
                case Section.Postgres: ConfigurePostgres(updates); break;
                case Section.AzureDocumentIntelligence: ConfigureAzureDocumentIntelligence(updates); break;
                case Section.AzureOpenAI: ConfigureAzureOpenAI(updates); break;
            }
        }

        if (updates.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No changes made.[/]");
            return 0;
        }

        var path = UserSecretsStore.GetSecretsFilePath(typeof(Program).Assembly);
        var root = UserSecretsStore.Load(path);
        foreach (var (section, values) in updates)
        {
            UserSecretsStore.SetSection(root, section, values);
        }

        UserSecretsStore.Save(path, root);

        AnsiConsole.MarkupLine($"[green]Saved configuration to[/] [grey]{Markup.Escape(path)}[/]");
        return 0;
    }

    private void ConfigurePostgres(Dictionary<string, IDictionary<string, string?>> updates)
    {
        var current = postgresOptions.Value;

        AnsiConsole.Write(new Rule("PostgreSQL").LeftJustified());

        var values = new Dictionary<string, string?>();
        AddIfChanged(values, "ConnectionString", PromptValue("Connection string", current.ConnectionString, secret: false));

        if (values.Count > 0)
        {
            updates["Postgres"] = values;
        }
    }

    private void ConfigureAzureDocumentIntelligence(Dictionary<string, IDictionary<string, string?>> updates)
    {
        var current = documentIntelligenceOptions.Value;

        AnsiConsole.Write(new Rule("Azure Document Intelligence").LeftJustified());

        var values = new Dictionary<string, string?>();
        AddIfChanged(values, "Endpoint", PromptValue("Endpoint", current.Endpoint, secret: false));
        AddIfChanged(values, "ApiKey", PromptValue("API key", current.ApiKey, secret: true));

        if (values.Count > 0)
        {
            updates["AzureDocumentIntelligence"] = values;
        }
    }

    private void ConfigureAzureOpenAI(Dictionary<string, IDictionary<string, string?>> updates)
    {
        var current = openAiOptions.Value;

        AnsiConsole.Write(new Rule("Azure OpenAI").LeftJustified());

        var values = new Dictionary<string, string?>();
        AddIfChanged(values, "Endpoint", PromptValue("Endpoint", current.Endpoint, secret: false));
        AddIfChanged(values, "ApiKey", PromptValue("API key", current.ApiKey, secret: true));
        AddIfChanged(values, "EmbeddingDeploymentName", PromptValue("Embedding deployment name", current.EmbeddingDeploymentName, secret: false));
        AddIfChanged(values, "ChatDeploymentName", PromptValue("Chat deployment name", current.ChatDeploymentName, secret: false));
        AddIfChanged(values, "VisionDeploymentName", PromptValue("Vision deployment name (blank = use chat deployment)", current.VisionDeploymentName, secret: false));

        if (values.Count > 0)
        {
            updates["AzureOpenAI"] = values;
        }
    }

    private static void AddIfChanged(Dictionary<string, string?> values, string key, string? value)
    {
        if (value is not null)
        {
            values[key] = value;
        }
    }

    private static string? PromptValue(string label, string? current, bool secret)
    {
        var hasCurrent = !string.IsNullOrWhiteSpace(current);
        var hint = secret
            ? (hasCurrent ? " [grey](leave blank to keep current)[/]" : string.Empty)
            : (hasCurrent ? $" [grey](current: {Markup.Escape(current!)})[/]" : string.Empty);

        var prompt = new TextPrompt<string>($"{label}:{hint}").AllowEmpty();

        if (secret)
        {
            prompt = prompt.Secret();
        }
        else if (hasCurrent)
        {
            prompt = prompt.DefaultValue(current!).ShowDefaultValue(false);
        }

        var input = AnsiConsole.Prompt(prompt);

        if (string.IsNullOrWhiteSpace(input))
        {
            return hasCurrent ? current : null;
        }

        return input;
    }
}
