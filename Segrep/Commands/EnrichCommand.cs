using Segrep.InterpreterModel;
using Segrep.Search;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class EnrichCommand(HybridSearch hybridSearch, InterpreterService interpreter)
    : AsyncCommand<EnrichCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<prompt>")]
        [System.ComponentModel.Description("The prompt to augment with retrieved context.")]
        public string Prompt { get; init; } = string.Empty;

        [CommandOption("--raw")]
        [System.ComponentModel.Description("Embed the prompt verbatim without LLM query expansion.")]
        public bool Raw { get; init; }

        [CommandOption("--top-k <N>")]
        [System.ComponentModel.Description("Number of document chunks to retrieve (default: 5).")]
        public int TopK { get; init; } = 5;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var searchQuery = settings.Raw
            ? settings.Prompt
            : await interpreter.ExpandPromptAsync(settings.Prompt, cancellationToken);

        var chunks = await hybridSearch.SearchAsync(searchQuery, topK: settings.TopK, cancellationToken: cancellationToken);

        // Emit an augmented prompt to stdout, ready to pipe into another LLM call.
        Console.Write(BuildAugmentedPrompt(settings.Prompt, chunks));
        return 0;
    }

    private static string BuildAugmentedPrompt(string originalPrompt, IReadOnlyList<SearchResult> chunks)
    {
        if (chunks.Count == 0)
            return originalPrompt;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("The following document excerpts are provided as context:");
        sb.AppendLine();
        foreach (var chunk in chunks)
        {
            var fileName = Path.GetFileName(chunk.FilePath);
            sb.AppendLine($"[source: {fileName} #{chunk.ChunkIndex}]");
            sb.AppendLine(chunk.ChunkText);
            sb.AppendLine();
        }
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(originalPrompt);
        return sb.ToString();
    }
}
