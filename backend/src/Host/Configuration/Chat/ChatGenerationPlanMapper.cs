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

    /// <summary>Builds the plan one capability's reference describes.</summary>
    /// <param name="settings">The bound declaration, already validated.</param>
    /// <param name="reference">The capability's own reference, which may name no model and take the main one.</param>
    /// <returns>The plan, or <see langword="null" /> when neither the reference nor the section resolves to a model.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The fallback is read from the reference rather than from the model it names, because which model stands behind
    /// another is a routing decision belonging to whoever chose the first one. A reference that names no model of its
    /// own takes the main model *and its fallback*, so a capability inherits the whole arrangement rather than half of
    /// it.
    /// </remarks>
    public static ChatGenerationPlan? Map(ChatModelOptions settings, ChatModelReferenceOptions reference)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(reference);

        var effective = reference.NamesModel ? reference : settings.MainModel;

        if (settings.FindModelFor(effective) is not { } model)
        {
            return null;
        }

        var plan = model.ToPlan();

        return settings.FindModel(effective.Fallback) is { } fallback
            ? plan.WithFallback(fallback.ToPlan())
            : plan;
    }
}
