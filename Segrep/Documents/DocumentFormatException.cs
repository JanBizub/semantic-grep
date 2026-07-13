namespace Segrep.Documents;

public sealed record DocumentFormatError(int Line, string Message);

public sealed class DocumentFormatException(IReadOnlyList<DocumentFormatError> errors)
    : Exception("The document does not conform to the required structure.")
{
    public IReadOnlyList<DocumentFormatError> Errors { get; } = errors;
}
