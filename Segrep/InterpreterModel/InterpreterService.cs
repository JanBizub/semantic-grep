using System.Text.Json;
using Microsoft.Extensions.AI;
using Segrep.Search;

namespace Segrep.InterpreterModel;

public sealed class InterpreterService(IChatClient chatClient)
{
    private const string InterpretSystemPrompt =
        "You are a query analyzer for a document search system. Given a user prompt, return ONLY a JSON object, " +
        "no markdown fences, with exactly two fields:\n" +
        "\"intent\": \"FOCUSED\" if the question targets specific facts or topics findable in a few passages; " +
        "\"CORPUS_WIDE\" if answering requires covering every indexed document (e.g. \"summarize each PDF\", " +
        "\"compare all reports\", \"list the documents\", \"what does each file say about X\"); " +
        "\"EXACT_TERM\" if the user asks to count or locate literal occurrences of a specific word or phrase " +
        "(e.g. \"how many times does Keter appear and on what pages\", \"find the word X in the documents\").\n" +
        "\"query\": for EXACT_TERM, exactly the literal word or phrase to search for — nothing else, no quotes; " +
        "otherwise the prompt rewritten as a focused, information-dense search query that surfaces relevant document passages.";

    private const string AnswerSystemPrompt =
        "You are a research assistant. Answer the user's question using only the provided document excerpts. " +
        "Cite each piece of information by copying the [source: ...] tag that precedes the excerpt verbatim, " +
        "including the page numbers when present (e.g. [source: report.pdf #3, p. 12]). " +
        "If no excerpt answers the question exactly, summarize the most closely related information the excerpts " +
        "do contain (e.g. adjacent programs, policies, or topics), clearly noting that it is related rather than " +
        "a direct answer. Only state that nothing relevant was found when the excerpts are truly unrelated.";

    private const string CorpusAnswerSystemPrompt =
        "You are a research assistant. The context contains excerpts from EVERY document indexed in the database, " +
        "grouped under a \"## Document:\" heading per file. Answer the user's question so that every listed document " +
        "is addressed; do not skip any. Use only the provided excerpts and cite by copying each excerpt's " +
        "[source: ...] tag verbatim, including the page numbers when present (e.g. [source: report.pdf #3, p. 12]). " +
        "If the excerpts for a document are insufficient for the question, say so for that document specifically.";

    public async Task<PromptInterpretation> InterpretPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, InterpretSystemPrompt),
            new(ChatRole.User, prompt),
        };

        try
        {
            var options = new ChatOptions { ResponseFormat = ChatResponseFormat.Json };
            var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
            return ParseInterpretation(response.Text, prompt);
        }
        catch
        {
            // Any failure (JSON mode unsupported, malformed output, transient error) degrades
            // to today's behavior: focused search over the verbatim prompt.
            return new PromptInterpretation(QueryIntent.Focused, prompt);
        }
    }

    public async Task<string> ComposeAnswerAsync(
        string prompt,
        IReadOnlyList<SearchResult> chunks,
        CancellationToken cancellationToken = default)
    {
        var context = ContextFormatter.BuildFlat(chunks);
        var userMessage = $"Question: {prompt}\n\nContext:\n{context}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, AnswerSystemPrompt),
            new(ChatRole.User, userMessage),
        };
        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return response.Text.Trim();
    }

    public async Task<string> ComposeCorpusAnswerAsync(
        string prompt,
        IReadOnlyList<SearchResult> chunks,
        CancellationToken cancellationToken = default)
    {
        var documents = ContextFormatter.DocumentNames(chunks);
        var context = ContextFormatter.BuildGrouped(chunks);
        var userMessage =
            $"Question: {prompt}\n\n" +
            $"The database contains {documents.Count} documents: {string.Join(", ", documents)}\n\n" +
            $"Context:\n{context}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, CorpusAnswerSystemPrompt),
            new(ChatRole.User, userMessage),
        };
        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return response.Text.Trim();
    }

    private static PromptInterpretation ParseInterpretation(string responseText, string originalPrompt)
    {
        var json = responseText.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            json = json.Trim('`');
            if (json.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                json = json[4..];
        }

        var parsed = JsonSerializer.Deserialize<InterpretationDto>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var intent = parsed?.Intent?.Trim().ToUpperInvariant() switch
        {
            "CORPUS_WIDE" => QueryIntent.CorpusWide,
            "EXACT_TERM" => QueryIntent.ExactTerm,
            _ => QueryIntent.Focused,
        };
        var query = string.IsNullOrWhiteSpace(parsed?.Query) ? originalPrompt : parsed.Query.Trim();
        return new PromptInterpretation(intent, query);
    }

    private sealed record InterpretationDto(string? Intent, string? Query);
}
