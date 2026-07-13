using Segrep.Update;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Segrep.Commands;

public sealed class UpdateCommand(SelfUpdater updater) : AsyncCommand<UpdateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--check")]
        [System.ComponentModel.Description("Report whether a newer release is available without downloading it.")]
        public bool CheckOnly { get; init; }

        [CommandOption("--force")]
        [System.ComponentModel.Description("Download and reinstall the latest release even if the current version is up to date.")]
        public bool Force { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        UpdateCheck check;
        try
        {
            check = await AnsiConsole.Status().StartAsync(
                "Checking for updates...",
                _ => updater.CheckAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Could not check for updates:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        if (check.LatestVersion is null)
        {
            AnsiConsole.MarkupLine($"[yellow]No releases published yet.[/] Current version: [bold]{Markup.Escape(check.CurrentVersion)}[/].");
            return 0;
        }

        AnsiConsole.MarkupLine(
            $"Current: [bold]{Markup.Escape(check.CurrentVersion)}[/]   Latest: [bold]{Markup.Escape(check.LatestVersion)}[/]");

        if (!check.UpdateAvailable && !settings.Force)
        {
            AnsiConsole.MarkupLine("[green]:check_mark: You are on the latest version.[/]");
            return 0;
        }

        if (settings.CheckOnly)
        {
            AnsiConsole.MarkupLine($"[yellow]Update available:[/] {Markup.Escape(check.LatestVersion)}. Run [bold]segrep update[/] to install.");
            return 0;
        }

        UpdateResult result;
        try
        {
            result = await AnsiConsole.Status().StartAsync("Updating...", async ctx =>
            {
                var progress = new Progress<string>(message => ctx.Status(Markup.Escape(message)));
                return await updater.DownloadAndReplaceAsync(check.Release!, progress, cancellationToken);
            });
        }
        catch (PlatformNotSupportedException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Update failed:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]:check_mark: {Markup.Escape(result.Message)}[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]{Markup.Escape(result.Message)}[/]");
        return 1;
    }
}
