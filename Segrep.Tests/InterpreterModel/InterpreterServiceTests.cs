using Segrep.InterpreterModel;
using Segrep.Search;

namespace Segrep.Tests.InterpreterModel;

public class InterpreterServiceTests
{
    private static Task<PromptInterpretation> Interpret(string llmResponse, string prompt = "the prompt") =>
        new InterpreterService(new FakeChatClient(llmResponse)).InterpretPromptAsync(prompt);

    [Fact]
    public async Task ParsesSingleFocusedTask()
    {
        var interpretation = await Interpret(
            """{"tasks":[{"intent":"FOCUSED","question":"What are the risks?","query":"key risks","document":null}]}""");

        var task = Assert.Single(interpretation.Tasks);
        Assert.Equal(QueryIntent.Focused, task.Intent);
        Assert.Equal("What are the risks?", task.Question);
        Assert.Equal("key risks", task.Query);
        Assert.Null(task.DocumentFilter);
    }

    [Fact]
    public async Task ParsesCompoundPromptIntoOrderedTasks()
    {
        var interpretation = await Interpret(
            """
            {"tasks":[
              {"intent":"EXACT_TERM","question":"How many times does Keter appear?","query":"Keter","document":"kaplan"},
              {"intent":"CORPUS_WIDE","question":"Summarize all books","query":"book summary","document":null}
            ]}
            """);

        Assert.Equal(2, interpretation.Tasks.Count);
        Assert.Equal(QueryIntent.ExactTerm, interpretation.Tasks[0].Intent);
        Assert.Equal("Keter", interpretation.Tasks[0].Query);
        Assert.Equal("kaplan", interpretation.Tasks[0].DocumentFilter);
        Assert.Equal(QueryIntent.CorpusWide, interpretation.Tasks[1].Intent);
    }

    [Fact]
    public async Task CapsTasksAtThree()
    {
        var tasks = string.Join(',', Enumerable.Range(1, 5)
            .Select(i => $$"""{"intent":"FOCUSED","question":"q{{i}}","query":"s{{i}}","document":null}"""));

        var interpretation = await Interpret($$"""{"tasks":[{{tasks}}]}""");

        Assert.Equal(3, interpretation.Tasks.Count);
        Assert.Equal("q1", interpretation.Tasks[0].Question);
        Assert.Equal("q3", interpretation.Tasks[2].Question);
    }

    [Fact]
    public async Task StripsMarkdownCodeFence()
    {
        var interpretation = await Interpret(
            "```json\n{\"tasks\":[{\"intent\":\"CORPUS_WIDE\",\"question\":\"q\",\"query\":\"s\",\"document\":null}]}\n```");

        Assert.Equal(QueryIntent.CorpusWide, Assert.Single(interpretation.Tasks).Intent);
    }

    [Fact]
    public async Task UnknownIntentDefaultsToFocused()
    {
        var interpretation = await Interpret(
            """{"tasks":[{"intent":"SOMETHING_ELSE","question":"q","query":"s","document":null}]}""");

        Assert.Equal(QueryIntent.Focused, Assert.Single(interpretation.Tasks).Intent);
    }

    [Fact]
    public async Task IntentMatchingIsCaseInsensitive()
    {
        var interpretation = await Interpret(
            """{"tasks":[{"intent":"exact_term","question":"q","query":"term","document":null}]}""");

        Assert.Equal(QueryIntent.ExactTerm, Assert.Single(interpretation.Tasks).Intent);
    }

    [Fact]
    public async Task MissingQuestionAndQueryFallBackToPrompt()
    {
        var interpretation = await Interpret(
            """{"tasks":[{"intent":"FOCUSED","question":"","query":" ","document":""}]}""",
            prompt: "original prompt");

        var task = Assert.Single(interpretation.Tasks);
        Assert.Equal("original prompt", task.Question);
        Assert.Equal("original prompt", task.Query);
        Assert.Null(task.DocumentFilter);
    }

    [Fact]
    public async Task MissingQueryFallsBackToQuestion()
    {
        var interpretation = await Interpret(
            """{"tasks":[{"intent":"FOCUSED","question":"the question","query":null,"document":null}]}""");

        Assert.Equal("the question", Assert.Single(interpretation.Tasks).Query);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"tasks\":[]}")]
    [InlineData("{\"other\":1}")]
    public async Task MalformedOrEmptyResponseDegradesToSingleFocusedTask(string llmResponse)
    {
        var interpretation = await Interpret(llmResponse, prompt: "my prompt");

        var task = Assert.Single(interpretation.Tasks);
        Assert.Equal(QueryIntent.Focused, task.Intent);
        Assert.Equal("my prompt", task.Question);
        Assert.Equal("my prompt", task.Query);
    }

    [Fact]
    public async Task ChatClientFailureDegradesToSingleFocusedTask()
    {
        var service = new InterpreterService(new FakeChatClient("", throwOnRequest: new InvalidOperationException("boom")));

        var interpretation = await service.InterpretPromptAsync("my prompt");

        var task = Assert.Single(interpretation.Tasks);
        Assert.Equal(QueryIntent.Focused, task.Intent);
        Assert.Equal("my prompt", task.Query);
    }

    [Fact]
    public async Task ComposeAnswerSendsQuestionAndFormattedContext()
    {
        var client = new FakeChatClient("  the answer  ");
        var service = new InterpreterService(client);
        var chunks = new List<SearchResult>
        {
            new(1, "/docs/report.pdf", "h1", 3, "chunk body", 0.9, PageStart: 12, PageEnd: 12),
        };

        var answer = await service.ComposeAnswerAsync("What is X?", chunks);

        Assert.Equal("the answer", answer);
        Assert.NotNull(client.LastMessages);
        var user = client.LastMessages[^1].Text;
        Assert.Contains("Question: What is X?", user);
        Assert.Contains("[source: report.pdf #3, p. 12]", user);
        Assert.Contains("chunk body", user);
    }

    [Fact]
    public async Task ComposeCorpusAnswerListsAllDocuments()
    {
        var client = new FakeChatClient("answer");
        var service = new InterpreterService(client);
        var chunks = new List<SearchResult>
        {
            new(1, "/docs/a.pdf", "h1", 0, "text a", 0.9),
            new(2, "/docs/b.pdf", "h2", 0, "text b", 0.8),
        };

        await service.ComposeCorpusAnswerAsync("Summarize each", chunks);

        var user = client.LastMessages![^1].Text;
        Assert.Contains("The database contains 2 documents: a.pdf, b.pdf", user);
        Assert.Contains("## Document: a.pdf", user);
        Assert.Contains("## Document: b.pdf", user);
    }

    [Fact]
    public async Task ComposeCompoundAnswerLabelsSectionsByIntent()
    {
        var client = new FakeChatClient("answer");
        var service = new InterpreterService(client);
        var sections = new List<CompoundSection>
        {
            new(new QueryTask(QueryIntent.ExactTerm, "How many Keter?", "Keter", null), "Keter: 7 occurrence(s)"),
            new(new QueryTask(QueryIntent.CorpusWide, "Summarize all books", "summary", null), "corpus context"),
            new(new QueryTask(QueryIntent.Focused, "What is X?", "x", null), "focused context"),
        };

        await service.ComposeCompoundAnswerAsync("compound question", sections);

        var user = client.LastMessages![^1].Text;
        Assert.Contains("## Part 1: How many Keter? (exact occurrence counts — authoritative)", user);
        Assert.Contains("## Part 2: Summarize all books (excerpts from every indexed document)", user);
        Assert.Contains("## Part 3: What is X? (document excerpts)", user);
        Assert.Contains("Keter: 7 occurrence(s)", user);
    }
}
