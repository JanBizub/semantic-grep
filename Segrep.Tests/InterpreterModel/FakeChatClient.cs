using Microsoft.Extensions.AI;

namespace Segrep.Tests.InterpreterModel;

/// <summary>
/// Test double for <see cref="IChatClient"/>: returns a canned response (or throws)
/// and records the messages of the last request.
/// </summary>
public sealed class FakeChatClient(string responseText, Exception? throwOnRequest = null) : IChatClient
{
    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }
    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        LastOptions = options;
        if (throwOnRequest is not null)
            throw throwOnRequest;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
