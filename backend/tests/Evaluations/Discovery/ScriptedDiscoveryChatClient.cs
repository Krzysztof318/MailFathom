// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Discovery;
using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Discovery;

/// <summary>A model that answers the Discover planning agent with a fixed plan and the composing agent with a result written from its turn.</summary>
/// <remarks>
/// Which agent is asking is read from the instruction the request carries rather than from how often it was asked, so a
/// run replayed from the store's cache reaches the same answers. The composition is a function of the turn because the
/// names of the sources it may cite are minted by the run, from what the retrieval returned.
/// </remarks>
/// <param name="plan">What the planning agent is answered with.</param>
/// <param name="composition">What the composing agent is answered with, given the turn it was handed.</param>
internal sealed class ScriptedDiscoveryChatClient(string plan, Func<string, string> composition) : IChatClient
{
    private const string ModelName = "scripted-discovery-model";

    /// <summary>Gets how many requests reached this model.</summary>
    public int Requests { get; private set; }

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.Requests++;

        var conversation = messages.ToList();
        var instructed = string.Concat(options?.Instructions, string.Concat(conversation.Select(static message => message.Text)));
        var answer = instructed.Contains(DiscoveryPlanningInstructions.Text, StringComparison.Ordinal)
            ? plan
            : composition(conversation[^1].Text);

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer))
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
