// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Retrieval;
using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Answering;

/// <summary>A model that looks mail up once with a fixed query, then answers with a fixed text.</summary>
/// <remarks>
/// Its reply is decided by the conversation it is handed rather than by how often it was asked — a lookup until a tool
/// result is in the conversation, the answer after — so a run replayed from the store's cache reaches the same answer
/// without this client being asked anything.
/// </remarks>
/// <param name="queryText">The lookup it writes, or <see langword="null" /> for a model that answers without looking anything up.</param>
/// <param name="answer">The text it answers with.</param>
internal sealed class ScriptedAnsweringChatClient(string? queryText, string answer) : IChatClient
{
    private const string ModelName = "scripted-answering-model";

    /// <summary>Gets how many requests reached this model.</summary>
    public int Requests { get; private set; }

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.Requests++;

        var looked = messages.SelectMany(static message => message.Contents).OfType<FunctionResultContent>().Any();
        var reply = queryText is not null && !looked
            ? new ChatMessage(
                ChatRole.Assistant,
                [
                    new FunctionCallContent(
                        "lookup-1",
                        ScopedMailKnowledgeRetrieval.SearchToolName,
                        new Dictionary<string, object?> { [ScopedMailKnowledgeRetrieval.QueryArgumentName] = queryText }),
                ])
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
