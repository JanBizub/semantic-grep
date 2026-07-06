using Segrep.InterpreterModel;
using Segrep.Search;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class AskCommand(HybridSearch hybridSearch, InterpreterService interpreter)
    : AsyncCommand<AskCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<prompt>")]
        [System.ComponentModel.Description("The question to answer from indexed documents.")]
        public string Prompt { get; init; } = string.Empty;

        [CommandOption("--top-k <N>")]
        [System.ComponentModel.Description("Number of document chunks to retrieve (default: 5).")]
        public int TopK { get; init; } = 5;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var searchQuery = await interpreter.ExpandPromptAsync(settings.Prompt, cancellationToken);
        var chunks = await hybridSearch.SearchAsync(searchQuery, topK: settings.TopK, cancellationToken: cancellationToken);

        if (chunks.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No relevant content found in the indexed documents.[/]");
            return 0;
        }

        var answer = await interpreter.ComposeAnswerAsync(settings.Prompt, chunks, cancellationToken);

        var questionPanel = new Panel(new Markup($"[blue]{Markup.Escape(settings.Prompt)}[/]"))
            .Header("[bold blue]Question[/]")
            .BorderColor(Color.Blue)
            .Expand();
        AnsiConsole.Write(questionPanel);

        var answerPanel = new Panel(new Markup(Markup.Escape(answer)))
            .Header("[bold]Answer[/]")
            .Expand();
        AnsiConsole.Write(answerPanel);
        return 0;
    }
}
