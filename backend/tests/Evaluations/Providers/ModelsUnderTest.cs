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
/// Every one of them is reached through <see cref="EvaluationEndpoint" />, which is also where the judge is reached:
/// one address and one key for the whole run.
/// </para>
/// </remarks>
internal static class ModelsUnderTest
{
    /// <summary>The variable carrying the models, separated by commas, whitespace, or line breaks.</summary>
    public const string ModelsVariable = "MAILFATHOM_EVALUATION_MODELS";

    /// <summary>The variable carrying the reasoning effort every model under test is asked for, where the run declares one.</summary>
    public const string ReasoningEffortVariable = "MAILFATHOM_EVALUATION_REASONING_EFFORT";

    /// <summary>The output budget one answer may occupy.</summary>
    /// <remarks>Room for the structured answer an agent writes and the reasoning a reasoning model spends before it.</remarks>
    private const int MaximumOutputTokens = 4096;

    /// <summary>Builds one plan per declared model.</summary>
    /// <returns>The plans, in the order the run declared the models.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run declared no model or an effort no provider could read.</exception>
    public static IReadOnlyList<ChatGenerationPlan> Plans()
    {
        var models = AiEvaluationRun.Required(ModelsVariable)
            .Split([',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (models.Length is 0)
        {
            throw new InvalidOperationException($"{ModelsVariable} names no model to measure.");
        }

        var address = EvaluationEndpoint.Address();
        var reasoningEffort = ParseReasoningEffort(AiEvaluationRun.Optional(ReasoningEffortVariable));

        return [.. models.Distinct(StringComparer.Ordinal).Select(model => PlanFor(model, address, reasoningEffort))];
    }

    /// <summary>Reads a declared reasoning effort.</summary>
    /// <param name="declared">The declaration, or <see langword="null" /> where the run made none.</param>
    /// <returns>The effort, or <see langword="null" /> where the run sends none.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the declaration is not a level a provider could read.</exception>
    /// <remarks>Held to the shape a deployment's own <c>ReasoningEffort</c> is, and the failure names the variable and never the value, as the judge's does.</remarks>
    public static string? ParseReasoningEffort(string? declared) =>
        declared is null || ChatGenerationPlan.IsUsableReasoningEffort(declared)
            ? declared
            : throw new InvalidOperationException(
                $"{ReasoningEffortVariable} is not a single word a provider could read as a reasoning level.");

    /// <summary>Builds the plan one model is measured with.</summary>
    /// <remarks>
    /// No sampling parameter is sent, for the reason the contract tests give: several current models refuse one outright,
    /// and what is measured is the request a deployment makes with nothing declared. A reasoning effort is sent only where
    /// the run declares one, which is the request a deployment declaring that effort makes.
    /// </remarks>
    private static ChatGenerationPlan PlanFor(string model, Uri? address, string? reasoningEffort)
    {
        var endpoint = new ChatEndpoint(
            "evaluation",
            address,
            model,
            ChatProviderApi.ChatCompletions,
            PublishedModelName: string.Empty);

        return ChatGenerationPlan.Create(
            endpoint,
            MaximumOutputTokens,
            temperature: null,
            topP: null,
            reasoningEffort,
            maximumMessagesPerRequest: 8,
            maximumRequestCharacters: 64_000,
            maximumRequestImageOctets: 4 * 1024 * 1024,
            requestTimeout: TimeSpan.FromMinutes(2));
    }
}
