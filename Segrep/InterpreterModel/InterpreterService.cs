using System.Text;
using Microsoft.Extensions.AI;
using Segrep.Search;

namespace Segrep.InterpreterModel;

public sealed class InterpreterService(IChatClient chatClient)
{
    private const string ExpandSystemPrompt =
        "You are a search query optimizer. Given a user prompt, rewrite it as a focused, information-dense search query " +
        "that surfaces relevant document passages. Return only the query text, no preamble.";

    private const string AnswerSystemPrompt =
        "You are a research assistant. Answer the user's question using only the provided document excerpts. " +
        "Cite each piece of information as [source: <filename> #<chunk_index>]. " +
        "If the excerpts do not contain enough information, say so explicitly.";

    public async Task<string> ExpandPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, ExpandSystemPrompt),
            new(ChatRole.User, prompt),
        };
        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return response.Text.Trim();
    }

    public async Task<string> ComposeAnswerAsync(
        string prompt,
        IReadOnlyList<SearchResult> chunks,
        CancellationToken cancellationToken = default)
    {
        var context = BuildContext(chunks);
        var userMessage = $"Question: {prompt}\n\nContext:\n{context}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AnswerSystemPrompt),
            new(ChatRole.User, userMessage),
        };
        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return response.Text.Trim();
    }

    private static string BuildContext(IReadOnlyList<SearchResult> chunks)
    {
        var sb = new StringBuilder();
        foreach (var chunk in chunks)
        {
            var fileName = Path.GetFileName(chunk.FilePath);
            sb.AppendLine($"[source: {fileName} #{chunk.ChunkIndex}]");
            sb.AppendLine(chunk.ChunkText);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
