// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Application.Chat;
using MailFathom.Host.Configuration.Chat;

namespace MailFathom.Evaluations.Providers;

/// <summary>Names the models a run measures one agent on, which all sit behind the one chat endpoint the provider-contract tests call.</summary>
/// <remarks>
/// <para>
/// A list rather than one model, because comparing models is the question this suite exists to answer in one run, and
/// one list per agent, because a deployment routes a model per agent. <see cref="EvaluationDeclaration" /> reads both from
/// the run's block, so the matrix is a loop over plans and needs no configuration file and no production change.
/// </para>
/// <para>
/// Every plan is the one a deployment builds for a model that declares what the block declares and nothing more: the
/// output budget, the request bounds, and the timeout are <see cref="ChatModelDeclarationOptions" />' own defaults, so a
/// verdict here is about what such a deployment would send and receive rather than about a budget of the suite's choosing.
/// </para>
/// <para>
/// Every one of them is reached through <see cref="EvaluationEndpoint" />, which is also where the judge is reached:
/// one address and one key for the whole run.
/// </para>
/// </remarks>
internal static class ModelsUnderTest
{
    /// <summary>Names the models the evaluations of one agent run on.</summary>
    /// <param name="capability">The agent the evaluation measures.</param>
    /// <returns>The models, in the order the run declared them.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable and the key, when the run's block cannot be read or names no model.</exception>
    public static IReadOnlyList<ModelUnderTest> For(ChatCapability capability) => EvaluationDeclaration.Read().ModelsFor(capability);

    /// <summary>Builds the plan one model is measured with where nothing but its name is declared.</summary>
    /// <param name="model">The routed model name, which is also the name its results are filed under.</param>
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
            Alias = model,
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
