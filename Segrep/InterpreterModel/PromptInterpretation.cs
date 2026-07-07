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

public sealed record PromptInterpretation(QueryIntent Intent, string ExpandedQuery);
