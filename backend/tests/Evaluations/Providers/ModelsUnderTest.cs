// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;

namespace MailFathom.Evaluations.Providers;

/// <summary>Reads the models a run measures, which all sit behind the one chat endpoint the provider-contract tests call.</summary>
/// <remarks>
/// <para>
/// A list rather than one model, because comparing two models is the question this suite exists to answer in one run.
/// Each becomes a <see cref="ChatGenerationPlan" /> built here, which is the declaration every agent takes through
/// dependency injection — so the matrix is a loop over plans and needs no configuration file and no production change.
/// </para>
/// <para>
/// The endpoint's address and key are the ones <c>vars.CHAT_PROVIDER_ADDRESS</c> and
/// <c>secrets.CHAT_PROVIDER_API_KEY</c> already supply, reached under the names the contract tests read them by.
/// </para>
/// </remarks>
internal static class ModelsUnderTest
{
    /// <summary>The variable carrying the models, separated by commas, whitespace, or line breaks.</summary>
    public const string ModelsVariable = "MAILFATHOM_EVALUATION_MODELS";

    private const string AddressVariable = "MAILFATHOM_CHAT_ADDRESS";
    private const string ApiKeyVariable = "MAILFATHOM_CHAT_API_KEY";

    /// <summary>The output budget one answer may occupy.</summary>
    /// <remarks>Room for the structured answer an agent writes and the reasoning a reasoning model spends before it.</remarks>
    private const int MaximumOutputTokens = 4096;

    /// <summary>Builds one plan per declared model.</summary>
    /// <returns>The plans, in the order the run declared the models.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run declared no model.</exception>
    public static IReadOnlyList<ChatGenerationPlan> Plans()
    {
        var models = AiEvaluationRun.Required(ModelsVariable)
            .Split([',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (models.Length is 0)
        {
            throw new InvalidOperationException($"{ModelsVariable} names no model to measure.");
        }

        var address = AiEvaluationRun.Optional(AddressVariable);

        return [.. models.Distinct(StringComparer.Ordinal).Select(model => PlanFor(model, address))];
    }

    /// <summary>Reads the key every model under test is reached with.</summary>
    /// <returns>The key.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run was asked for without one.</exception>
    public static string ApiKey() => AiEvaluationRun.Required(ApiKeyVariable);

    /// <summary>Builds the plan one model is measured with.</summary>
    /// <remarks>
    /// Neither sampling parameter nor a reasoning effort is sent, for the reason the contract tests give: several current
    /// models refuse one outright, and what is measured is the request a deployment makes with nothing declared.
    /// </remarks>
    private static ChatGenerationPlan PlanFor(string model, string? address)
    {
        var endpoint = new ChatEndpoint(
            "evaluation",
            address is null ? null : new Uri(address, UriKind.Absolute),
            model,
            ChatProviderApi.ChatCompletions,
            PublishedModelName: string.Empty);

        return ChatGenerationPlan.Create(
            endpoint,
            MaximumOutputTokens,
            temperature: null,
            topP: null,
            reasoningEffort: null,
            maximumMessagesPerRequest: 8,
            maximumRequestCharacters: 64_000,
            maximumRequestImageOctets: 4 * 1024 * 1024,
            requestTimeout: TimeSpan.FromMinutes(2));
    }
}
