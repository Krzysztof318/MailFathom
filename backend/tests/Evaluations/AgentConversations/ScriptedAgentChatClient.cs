// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>A model that makes a fixed set of tool calls once, then answers with a fixed text.</summary>
/// <remarks>
/// Its reply is decided by the conversation it is handed rather than by how often it was asked — the calls until a tool
/// result is in the conversation, the answer after — so a run replayed from the store's cache reaches the same answer.
/// </remarks>
/// <param name="calls">Each tool it calls, by name, with the arguments it passes; empty for a model that calls none.</param>
/// <param name="answer">The text it answers with.</param>
/// <param name="callsEveryTurn">Whether it makes its calls again on every turn and so never answers, which is a model that runs until its bounds stop it.</param>
internal sealed class ScriptedAgentChatClient(
    IReadOnlyList<(string Tool, IDictionary<string, object?> Arguments)> calls,
    string answer,
    bool callsEveryTurn = false) : IChatClient
{
    private const string ModelName = "scripted-agent-model";

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var called = messages.SelectMany(static message => message.Contents).OfType<FunctionResultContent>().Any();
        var reply = calls.Count > 0 && (callsEveryTurn || !called)
            ? new ChatMessage(
                ChatRole.Assistant,
                [.. calls.Select(static (call, index) => new FunctionCallContent($"call-{index}", call.Tool, call.Arguments))])
            : new ChatMessage(ChatRole.Assistant, answer);

        return Task.FromResult(new ChatResponse(reply)
        {
            ModelId = ModelName,
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15 },
        });
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Nothing here streams.");

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(ChatClientMetadata)
            ? new ChatClientMetadata("scripted", defaultModelId: ModelName)
            : null;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
