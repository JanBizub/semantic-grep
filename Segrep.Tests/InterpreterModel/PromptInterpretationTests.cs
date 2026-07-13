using Segrep.InterpreterModel;

namespace Segrep.Tests.InterpreterModel;

public class PromptInterpretationTests
{
    [Fact]
    public void SingleCreatesOneTaskWithPromptAsQuestionAndQuery()
    {
        var interpretation = PromptInterpretation.Single(QueryIntent.Focused, "find the risks");

        var task = Assert.Single(interpretation.Tasks);
        Assert.Equal(QueryIntent.Focused, task.Intent);
        Assert.Equal("find the risks", task.Question);
        Assert.Equal("find the risks", task.Query);
        Assert.Null(task.DocumentFilter);
    }
}
