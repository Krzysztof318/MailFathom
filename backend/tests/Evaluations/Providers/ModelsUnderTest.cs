// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Application.Chat;
using MailFathom.Host.Configuration.Chat;

namespace MailFathom.Evaluations.Providers;

/// <summary>Reads the models a run measures, which all sit behind the one chat endpoint the provider-contract tests call.</summary>
/// <remarks>
/// <para>
/// A list rather than one model, because comparing two models is the question this suite exists to answer in one run.
/// Each becomes a <see cref="ChatGenerationPlan" /> built here, which is the declaration every agent takes through
/// dependency injection — so the matrix is a loop over plans and needs no configuration file and no production change.
/// </para>
/// <para>
/// Every plan is the one a deployment builds for a model that declares nothing but its name and its address: the output
/// budget, the request bounds, and the timeout are <see cref="ChatModelDeclarationOptions" />' own defaults, so a
/// verdict here is about what such a deployment would send and receive rather than about a budget of the suite's choosing.
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
    /// <param name="model">The routed model name.</param>
    /// <param name="address">The endpoint the model sits behind, or <see langword="null" /> for the provider's own default.</param>
    /// <param name="reasoningEffort">The reasoning effort the run declares, or <see langword="null" /> where it declares none.</param>
    /// <returns>The plan.</returns>
    /// <remarks>
    /// No sampling parameter is sent, for the reason the contract tests give: several current models refuse one outright,
    /// and what is measured is the request a deployment makes with nothing declared. A reasoning effort is sent only where
    /// the run declares one, which is the request a deployment declaring that effort makes.
    /// </remarks>
    public static ChatGenerationPlan PlanFor(string model, Uri? address = null, string? reasoningEffort = null) =>
        new ChatModelDeclarationOptions
        {
            Alias = "evaluation",
            Model = model,
            Address = address?.AbsoluteUri ?? string.Empty,
            ReasoningEffort = reasoningEffort,
        }.ToPlan();

    /// <summary>Refuses one turn past what the plan's model accepts, as a deployment's agent refuses it rather than sending it.</summary>
    /// <param name="turn">The turn a scenario composed through the agent's own composition.</param>
    /// <param name="plan">The plan the model is measured with.</param>
    /// <exception cref="ChatGenerationFailedException">Thrown when the turn exceeds what the model declares.</exception>
    public static void RequireOneTurn(string turn, ChatGenerationPlan plan) =>
        ChatRequestBounds.RequireForAttempt([new ChatMessage(ChatRole.User, turn)], plan);
}
