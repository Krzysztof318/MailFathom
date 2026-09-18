// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Enrichment;

/// <summary>A provider that answers every request with one fixed text and says of itself whatever it was told to.</summary>
/// <param name="answer">The text every response carries.</param>
/// <param name="metadata">What the client publishes about itself, which is where a real one names its address and model.</param>
internal sealed class ScriptedChatClient(string answer, ChatClientMetadata metadata) : IChatClient
{
    /// <summary>Gets how many requests reached this provider.</summary>
    public int Requests { get; private set; }

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.Requests++;

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer))
        {
            ModelId = metadata.DefaultModelId,
            AdditionalProperties = new AdditionalPropertiesDictionary { ["provider"] = metadata.ProviderUri?.Host },
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
        serviceKey is null && serviceType == typeof(ChatClientMetadata) ? metadata : null;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
