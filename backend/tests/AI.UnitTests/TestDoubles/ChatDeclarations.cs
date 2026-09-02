// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Builds the endpoints and plans the chat tests declare, so each test states only what it varies.</summary>
internal static class ChatDeclarations
{
    /// <summary>The deadline every plan below applies to one request unless a test says otherwise.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Builds the declared endpoint.</summary>
    public static ChatEndpoint Endpoint(
        string alias = "answering",
        string? address = "https://provider.invalid/v1/",
        string routedModelName = "a-chat-model",
        ChatProviderApi api = ChatProviderApi.ChatCompletions) =>
        new(alias, address is null ? null : new Uri(address, UriKind.Absolute), routedModelName, api);

    /// <summary>Builds a plan over the declared endpoint.</summary>
    public static ChatGenerationPlan Plan(
        ChatEndpoint? endpoint = null,
        int maximumOutputTokens = 256,
        float? temperature = null,
        float? topP = null,
        string? reasoningEffort = null,
        int maximumMessagesPerRequest = 8,
        int maximumRequestCharacters = 4000,
        TimeSpan? requestTimeout = null) =>
        ChatGenerationPlan.Create(
            endpoint ?? Endpoint(),
            maximumOutputTokens,
            temperature,
            topP,
            reasoningEffort,
            maximumMessagesPerRequest,
            maximumRequestCharacters,
            requestTimeout ?? RequestTimeout);

    /// <summary>Publishes one fixed plan, standing in for the composition root's reading of the declaration in force.</summary>
    public static IChatGenerationPlanSource PlanSource(ChatGenerationPlan? plan = null) =>
        new FixedPlanSource(plan ?? Plan());

    private sealed class FixedPlanSource(ChatGenerationPlan plan) : IChatGenerationPlanSource
    {
        public ChatGenerationPlan Current => plan;
    }
}
