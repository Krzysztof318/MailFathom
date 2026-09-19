// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Enrichment;

/// <summary>A provider that answers every request with one fixed text and says of itself whatever it was told to.</summary>
/// <remarks>
/// Hand-written rather than substituted because its behaviour is the point rather than its answers: it plants the
/// values a real client publishes about itself into <em>every</em> channel one of them can leave by — the response's
/// <see cref="ChatResponse.ModelId" />, its <see cref="ChatResponse.AdditionalProperties" />, and the
/// <see cref="ChatClientMetadata" /> it answers <see cref="IChatClient.GetService" /> with — which is what makes the
/// anonymity test a measurement rather than an assertion that one arrangement was enough. It also narrows the surface
/// deliberately: streaming throws, so a path that quietly streamed past the run's response cache fails loudly.
/// </remarks>
/// <param name="answer">The text every response carries.</param>
/// <param name="metadata">What the client publishes about itself, which is where a real one names its address and model.</param>
internal sealed class ScriptedChatClient(string answer, ChatClientMetadata metadata) : IChatClient
{
    /// <summary>Gets how many requests reached this provider.</summary>
    public int Requests { get; private set; }

    /// <summary>Gets the options every request that reached this provider carried, in the order they arrived.</summary>
    public List<ChatOptions?> ReceivedOptions { get; } = [];

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.Requests++;
        this.ReceivedOptions.Add(options);

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
