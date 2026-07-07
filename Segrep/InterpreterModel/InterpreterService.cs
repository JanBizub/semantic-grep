using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Segrep.Search;

namespace Segrep.InterpreterModel;

public sealed class InterpreterService(IChatClient chatClient)
{
    private const string InterpretSystemPrompt =
        "You are a query analyzer for a document search system. Given a user prompt, return ONLY a JSON object, " +
        "no markdown fences, with exactly one field \"tasks\": an array of 1 to 3 task objects. Most prompts need " +
        "exactly ONE task; split into multiple tasks only when the prompt contains clearly distinct sub-requests " +
        "that need different retrieval strategies (e.g. counting a word AND summarizing documents).\n" +
        "Each task object has exactly four fields:\n" +
        "\"intent\": \"FOCUSED\" if the sub-request targets specific facts or topics findable in a few passages; " +
        "\"CORPUS_WIDE\" if answering it requires covering every indexed document (e.g. \"summarize each PDF\", " +
        "\"compare all reports\", \"list the documents\", \"what does each file say about X\"); " +
        "\"EXACT_TERM\" if the sub-request asks to count or locate literal occurrences of a specific word or phrase " +
        "(e.g. \"how many times does Keter appear and on what pages\").\n" +
        "\"question\": the sub-request restated as a self-contained question.\n" +
        "\"query\": for EXACT_TERM, exactly the literal word or phrase to search for — nothing else, no quotes; " +
        "otherwise the sub-request rewritten as a focused, information-dense search query that surfaces relevant passages.\n" +
        "\"document\": if the sub-request names one specific document, book, or file, a short single-word lowercase " +
        "fragment of its likely file name (e.g. \"Kaplan's book\" -> \"kaplan\"); otherwise null.";

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

    private const string CompoundAnswerSystemPrompt =
        "You are a research assistant answering a multi-part question. The context is organized into sections, " +
        "one per part of the question, each headed \"## Part N: <sub-question>\".\n" +
        "Rules:\n" +
        "- Answer EVERY part of the user's question; do not skip any part.\n" +
        "- Sections marked \"exact occurrence counts — authoritative\" contain exact numbers computed by the system: " +
        "reproduce them faithfully and completely — do not re-count, re-derive, round, or omit occurrences or page numbers.\n" +
        "- For document-excerpt sections, use only the provided excerpts and cite by copying each excerpt's " +
        "[source: ...] tag verbatim, including page numbers when present.\n" +
        "- When a section covers every indexed document, address every listed document; if the excerpts for a " +
        "document are insufficient, say so for that document specifically.\n" +
        "- If the user asks for a table, format it as a Markdown table.";

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
            // to the pre-decomposition behavior: one focused search over the verbatim prompt.
            return PromptInterpretation.Single(QueryIntent.Focused, prompt);
        }
    }

    public async Task<string> ComposeAnswerAsync(
        string prompt,
        IReadOnlyList<SearchResult> chunks,
        CancellationToken cancellationToken = default)
    {
        var context = ContextFormatter.BuildFlat(chunks);
        var userMessage = $"Question: {prompt}\n\nContext:\n{context}";
        return await ComposeAsync(AnswerSystemPrompt, userMessage, cancellationToken);
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
        return await ComposeAsync(CorpusAnswerSystemPrompt, userMessage, cancellationToken);
    }

    /// <summary>
    /// Option B compound composition: every sub-task's retrieval result goes into ONE
    /// call that writes a single coherent answer covering all parts of the question.
    /// </summary>
    public async Task<string> ComposeCompoundAnswerAsync(
        string prompt,
        IReadOnlyList<CompoundSection> sections,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Question: {prompt}");
        sb.AppendLine();
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var label = section.Task.Intent switch
            {
                QueryIntent.ExactTerm => "exact occurrence counts — authoritative",
                QueryIntent.CorpusWide => "excerpts from every indexed document",
                _ => "document excerpts",
            };
            sb.AppendLine($"## Part {i + 1}: {section.Task.Question} ({label})");
            sb.AppendLine();
            sb.AppendLine(section.Context.TrimEnd());
            sb.AppendLine();
        }

        return await ComposeAsync(CompoundAnswerSystemPrompt, sb.ToString(), cancellationToken);
    }

    private async Task<string> ComposeAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
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

        var tasks = (parsed?.Tasks ?? [])
            .Where(t => t is not null)
            .Take(3)
            .Select(t => MapTask(t, originalPrompt))
            .ToList();

        return tasks.Count == 0
            ? PromptInterpretation.Single(QueryIntent.Focused, originalPrompt)
            : new PromptInterpretation(tasks);
    }

    private static QueryTask MapTask(TaskDto dto, string originalPrompt)
    {
        var intent = dto.Intent?.Trim().ToUpperInvariant() switch
        {
            "CORPUS_WIDE" => QueryIntent.CorpusWide,
            "EXACT_TERM" => QueryIntent.ExactTerm,
            _ => QueryIntent.Focused,
        };
        var question = string.IsNullOrWhiteSpace(dto.Question) ? originalPrompt : dto.Question.Trim();
        var query = string.IsNullOrWhiteSpace(dto.Query) ? question : dto.Query.Trim();
        var document = string.IsNullOrWhiteSpace(dto.Document) ? null : dto.Document.Trim();
        return new QueryTask(intent, question, query, document);
    }

    private sealed record TaskDto(string? Intent, string? Question, string? Query, string? Document);

    private sealed record InterpretationDto(List<TaskDto>? Tasks);
}
