namespace Segrep.InterpreterModel;

public enum QueryIntent
{
    /// <summary>The question targets specific facts findable in a few passages.</summary>
    Focused,

    /// <summary>Answering requires coverage of every indexed document (e.g. "summarize each PDF").</summary>
    CorpusWide,
}

public sealed record PromptInterpretation(QueryIntent Intent, string ExpandedQuery);
