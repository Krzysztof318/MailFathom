// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.AI.Chat;
using Microsoft.Extensions.DependencyInjection;

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
        ChatProviderApi api = ChatProviderApi.ChatCompletions,
        string publishedModelName = "",
        bool stickySessions = false) =>
        new(
            alias,
            address is null ? null : new Uri(address, UriKind.Absolute),
            routedModelName,
            api,
            publishedModelName,
            stickySessions);

    /// <summary>Builds a plan over the declared endpoint.</summary>
    public static ChatGenerationPlan Plan(
        ChatEndpoint? endpoint = null,
        int maximumOutputTokens = 256,
        float? temperature = null,
        float? topP = null,
        string? reasoningEffort = null,
        int maximumMessagesPerRequest = 8,
        int maximumRequestCharacters = 4000,
        int maximumRequestImageOctets = 1024,
        TimeSpan? requestTimeout = null,
        IReadOnlyDictionary<string, JsonElement>? additionalProperties = null) =>
        ChatGenerationPlan.Create(
            endpoint ?? Endpoint(),
            maximumOutputTokens,
            temperature,
            topP,
            reasoningEffort,
            maximumMessagesPerRequest,
            maximumRequestCharacters,
            maximumRequestImageOctets,
            requestTimeout ?? RequestTimeout,
            additionalProperties);

    /// <summary>Publishes one fixed plan, standing in for the composition root's reading of the declaration in force.</summary>
    /// <param name="plan">The plan every capability the routing does not name resolves to.</param>
    /// <param name="routed">The capabilities an operator sent to a model of their own, standing in for a declaration that wrote those keys.</param>
    public static IChatGenerationPlanSource PlanSource(
        ChatGenerationPlan? plan = null,
        IReadOnlyDictionary<ChatCapability, ChatGenerationPlan>? routed = null) =>
        new FixedPlanSource(plan ?? Plan(), routed);

    /// <summary>Registers the plans a composition root publishes: the main model, and the one each capability is routed to.</summary>
    /// <param name="services">The collection under test.</param>
    /// <param name="plan">The plan every capability resolves to, standing in for a declaration routing none of them elsewhere.</param>
    /// <param name="routed">The capabilities sent to a model of their own instead.</param>
    /// <remarks>Both shapes, because an agent asks for its capability's plan by key while the chat client takes the main one.</remarks>
    public static void AddPlans(
        IServiceCollection services,
        ChatGenerationPlan? plan = null,
        IReadOnlyDictionary<ChatCapability, ChatGenerationPlan>? routed = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(PlanSource(plan, routed));
        services.AddScoped(provider => provider.GetRequiredService<IChatGenerationPlanSource>().Current);

        foreach (var capability in Enum.GetValues<ChatCapability>())
        {
            services.AddKeyedScoped(
                capability,
                (provider, key) => provider.GetRequiredService<IChatGenerationPlanSource>()
                    .PlanFor((ChatCapability)key!));
        }
    }

    private sealed class FixedPlanSource(
        ChatGenerationPlan plan,
        IReadOnlyDictionary<ChatCapability, ChatGenerationPlan>? routed) : IChatGenerationPlanSource
    {
        public ChatGenerationPlan Current => plan;

        public ChatGenerationPlan PlanFor(ChatCapability capability) =>
            routed is not null && routed.TryGetValue(capability, out var own) ? own : plan;
    }
}
