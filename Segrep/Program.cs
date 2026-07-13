using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Segrep.Chunking;
using Segrep.Commands;
using Segrep.Configuration;
using Segrep.DocumentIntelligence;
using Segrep.Embeddings;
using Segrep.Infrastructure;
using Segrep.InterpreterModel;
using Segrep.Search;
using Segrep.Store;
using Segrep.Update;
using Spectre.Console.Cli;

namespace Segrep;

public static class Program
{
    public static int Main(string[] args)
    {
        var configuration = AppConfiguration.Build();

        var services = new ServiceCollection();
        services.AddAppConfiguration(configuration);
        services.AddPostgresStore();
        services.AddAzureDocumentIntelligence();
        services.AddEmbeddingModel();
        services.AddInterpreterModel();
        services.AddSingleton<SemanticSearch>();
        services.AddSingleton<FullTextSearch>();
        services.AddSingleton<GrepSearch>();
        services.AddSingleton<HybridSearch>();
        services.AddSingleton<TermSearch>();
        services.AddSingleton<MarkdownChunker>();
        services.AddSingleton<InterpreterService>();
        services.AddSelfUpdate();

        using var provider = services.BuildServiceProvider();
        EnsureStoreSchema(provider, configuration);

        var registrar = new DependencyInjectionRegistrar(services);
        var app = new CommandApp(registrar);
        app.Configure(config =>
        {
            config.SetApplicationName("segrep");
            config.SetApplicationVersion(VersionInfo.Current);

            config.AddExample("index", "./docs");
            config.AddExample("ask", "\"What are the key risks mentioned in the reports?\"");
            config.AddExample("enrich", "\"Summarise the Q3 results\"", "--raw");
            config.AddExample("find", "\"Keter\"");
            config.AddExample("configure");
            config.AddExample("status");
            config.AddExample("update", "--check");

            config.AddCommand<IndexCommand>("index")
                .WithDescription("Parse, chunk, embed, and store documents from a folder or file.");

            config.AddCommand<EnrichCommand>("enrich")
                .WithDescription("Augment a prompt with retrieved context; pipe the result into another LLM.");

            config.AddCommand<AskCommand>("ask")
                .WithDescription("Ask a question — returns a cited answer composed from indexed documents.");

            config.AddCommand<FindCommand>("find")
                .WithDescription("Count exact occurrences of a word or phrase across indexed documents, with page numbers.");

            config.AddCommand<ConfigureCommand>("configure")
                .WithDescription("Interactively set Azure Document Intelligence, Azure OpenAI, and PostgreSQL credentials.");

            config.AddCommand<StatusCommand>("status")
                .WithDescription("Check connectivity to all configured services.");

            config.AddCommand<ClearCommand>("clear")
                .WithDescription("Delete all indexed chunks from the database.");

            config.AddCommand<ListCommand>("list")
                .WithDescription("Show all indexed documents.");

            config.AddCommand<UpdateCommand>("update")
                .WithDescription("Download and install the latest segrep release.");
        });

        return app.Run(args);
    }

    private static void EnsureStoreSchema(IServiceProvider provider, IConfiguration configuration)
    {
        var connectionString = configuration.GetSection(PostgresOptions.SectionName)["ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        try
        {
            var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
            SchemaMigrator.EnsureCreatedAsync(dataSource).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Spectre.Console.AnsiConsole.MarkupLine($"[yellow]Warning:[/] could not reach PostgreSQL to apply schema migrations: {Spectre.Console.Markup.Escape(ex.Message)}");
        }
    }
}