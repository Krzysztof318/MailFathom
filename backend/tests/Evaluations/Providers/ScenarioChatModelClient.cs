// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.ProviderAdapters;
using MailFathom.Application.Chat;

namespace MailFathom.Evaluations.Providers;

/// <summary>The chat port a component that is not an agent calls, answered by the model under test.</summary>
/// <remarks>
/// <para>
/// A deployment answers this port through <see cref="ProviderChatModelClient" />, and a scenario keeps the two halves of
/// that path that decide what is sent: the conversation mapping and the options the plan maps to. What it leaves out is
/// what decides whether a call happens rather than what it says — the resilience budget, the fallback chain, the egress
/// guard, and the health record — for the reason <see cref="ProviderChatClient" /> gives.
/// </para>
/// <para>
/// An answer with no text is refused the way a deployment refuses one, so a model that answers nothing ends a judging
/// pass exactly as it would in production rather than reading as a judgement.
/// </para>
/// <para>
/// The client library's namespace is written out at every use rather than imported, because it publishes a
/// <c>ChatMessage</c> of its own and this file reads both.
/// </para>
/// </remarks>
/// <param name="model">The model under test, behind the run's response cache, which stays the caller's.</param>
/// <param name="plan">The plan the model is measured with.</param>
internal sealed class ScenarioChatModelClient(Microsoft.Extensions.AI.IChatClient model, ChatGenerationPlan plan) : IChatModelClient
{
    /// <inheritdoc />
    public async Task<ChatAnswer> AnswerAsync(IReadOnlyList<ChatMessage> conversation, CancellationToken cancellationToken)
    {
        var response = await model.GetResponseAsync(
            ChatConversationMapping.ToProviderConversation(conversation),
            ChatGenerationParameterMapping.ToChatOptions(plan),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Text))
        {
            throw new ChatGenerationFailedException(plan.Endpoint.Alias, ChatGenerationFailure.AnswerEmpty);
        }

        return new ChatAnswer(response.Text, ChatGenerationStop.Unreported, Usage: null);
    }
}
