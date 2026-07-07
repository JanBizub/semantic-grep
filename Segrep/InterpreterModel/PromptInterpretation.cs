namespace Segrep.InterpreterModel;

public enum QueryIntent
{
    /// <summary>The question targets specific facts findable in a few passages.</summary>
    Focused,

    /// <summary>Answering requires coverage of every indexed document (e.g. "summarize each PDF").</summary>
    CorpusWide,

    /// <summary>
    /// The user wants literal occurrences of a specific term counted/located
    /// (e.g. "how many times does 'Keter' appear and on what pages").
    /// </summary>
    ExactTerm,
}

/// <summary>
/// One retrieval sub-task decomposed from the user's prompt.
/// </summary>
/// <param name="Intent">Which retrieval strategy the sub-task needs.</param>
/// <param name="Question">The sub-request restated as a self-contained question (used for answer composition).</param>
/// <param name="Query">The search string — for <see cref="QueryIntent.ExactTerm"/>, the literal term to count.</param>
/// <param name="DocumentFilter">Optional file-name substring when the sub-request names a specific document, else null.</param>
public sealed record QueryTask(QueryIntent Intent, string Question, string Query, string? DocumentFilter);

/// <summary>
/// The interpreter's decomposition of a prompt into 1–3 ordered sub-tasks.
/// Most prompts yield a single task; compound prompts (e.g. an exact-term count plus a
/// corpus-wide summary) yield one task per distinct sub-request.
/// </summary>
public sealed record PromptInterpretation(IReadOnlyList<QueryTask> Tasks)
{
    public static PromptInterpretation Single(QueryIntent intent, string prompt) =>
        new([new QueryTask(intent, prompt, prompt, null)]);
}
