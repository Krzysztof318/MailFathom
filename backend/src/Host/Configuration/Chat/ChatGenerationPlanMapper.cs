// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Turns a reference into the plan the provider adapter runs on, resolving the model it names and the one behind it.</summary>
/// <remarks>
/// The mapping is separate from the options type for the reason every mapper in this directory is: the bound object is
/// mutable, binder-shaped, and full of empty strings that mean absence, while the plan is the validated value the
/// adapter is allowed to assume. Keeping the two apart is what lets the adapter hold no defaulting logic at all.
/// </remarks>
internal static class ChatGenerationPlanMapper
{
    /// <summary>Builds the plan the deployment's main model describes.</summary>
    /// <param name="settings">The bound declaration, already validated.</param>
    /// <returns>The plan, or <see langword="null" /> when the deployment declared no chat provider.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Nothing declared is not a failure. An instance that has not chosen a chat provider serves every read path exactly
    /// as it did before, and returning nothing is what lets the composition root register no client rather than one that
    /// fails at first use.
    /// </remarks>
    public static ChatGenerationPlan? Map(ChatModelOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Map(settings, settings.MainModel);
    }

    /// <summary>Builds the plan one capability runs on.</summary>
    /// <param name="settings">The bound declaration, already validated.</param>
    /// <param name="capability">The work whose reference is read.</param>
    /// <returns>The plan, or <see langword="null" /> when neither the reference nor the section resolves to a model.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    public static ChatGenerationPlan? Map(ChatModelOptions settings, ChatCapability capability)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Map(settings, settings.ReferenceFor(capability));
    }

    /// <summary>Builds the plan one capability's reference describes.</summary>
    /// <param name="settings">The bound declaration, already validated.</param>
    /// <param name="reference">The capability's own reference, which may name no model and take the main one.</param>
    /// <returns>The plan, or <see langword="null" /> when neither the reference nor the section resolves to a model.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The fallback is read from the reference rather than from the model it names, because which model stands behind
    /// another is a routing decision belonging to whoever chose the first one. A reference that names no model of its
    /// own takes the main model *and its fallback*, which is what a deployment declaring one model and writing none of
    /// these keys goes on doing.
    /// </para>
    /// <para>
    /// A reference that does name a model resolves to that model, then to the fallback beside it, then to the main
    /// model — three at most, and the main model's own fallback is not attempted after that, because a capability's
    /// operator asked for this model and named what stands behind it, and the deployment's answer to *everything else
    /// failed* is the model it answers questions with. A model already in the chain is not attempted twice, so a
    /// reference naming the main model, or naming it as its own fallback, stops there rather than repeating it.
    /// </para>
    /// </remarks>
    public static ChatGenerationPlan? Map(ChatModelOptions settings, ChatModelReferenceOptions reference)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(reference);

        ChatModelDeclarationOptions?[] wanted = reference.NamesModel
            ?
            [
                settings.FindModel(reference.Alias),
                settings.FindModel(reference.Fallback),
                settings.FindMainModel(),
            ]
            :
            [
                settings.FindMainModel(),
                settings.FindModel(settings.MainModel.Fallback),
            ];

        if (wanted[0] is null)
        {
            return null;
        }

        // Distinct by alias, because a capability routed to the main model, or naming it as its own fallback, is an
        // ordinary declaration asking for a chain that stops there rather than one that asks the same endpoint twice.
        var chain = wanted
            .OfType<ChatModelDeclarationOptions>()
            .DistinctBy(model => model.Alias.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Assembled from the back, because a plan carries the model behind it rather than the one in front.
        ChatGenerationPlan? plan = null;

        for (var position = chain.Length - 1; position >= 0; position--)
        {
            var model = chain[position].ToPlan();
            plan = plan is null ? model : model.WithFallback(plan);
        }

        return plan;
    }
}
