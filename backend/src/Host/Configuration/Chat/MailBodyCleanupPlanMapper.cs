// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.BodyCleanup;
using MailFathom.AI.Chat;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Turns the bound declaration into the plan the body-cleanup pass runs on.</summary>
/// <remarks>
/// The mapping is separate from the options type for the reason every mapper in this directory is: the bound object is
/// mutable, binder-shaped, and carries empty strings that mean absence, while the plan is the validated value the pass is
/// allowed to assume. What this one adds over <see cref="ChatGenerationPlanMapper" /> is the one substitution the block
/// exists for — the routed model — and it makes it here rather than inside the pass so the pass holds no defaulting logic.
/// </remarks>
internal static class MailBodyCleanupPlanMapper
{
    /// <summary>Builds the plan a declared body-cleanup pass describes.</summary>
    /// <param name="settings">The bound chat declaration, already validated.</param>
    /// <returns>The plan, or <see langword="null" /> when this deployment declared no chat provider.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A model nobody wrote resolves to the endpoint's own, which is what keeps the common declaration — a chat endpoint
    /// and nothing else — from needing a key to say "the same model as everything else".
    /// </remarks>
    public static MailBodyCleanupPlan? Map(ChatModelOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsConfigured)
        {
            return null;
        }

        var plan = ChatGenerationPlanMapper.Map(settings)
            ?? throw new InvalidOperationException(
                "The chat endpoint is declared and the generation plan is absent from the same declaration.");

        return new MailBodyCleanupPlan(
            settings.BodyCleanup.Model.Trim() is { Length: > 0 } routedModel
                ? ChatGenerationPlan.Create(
                    plan.Endpoint with { RoutedModelName = routedModel },
                    plan.MaximumOutputTokens,
                    plan.Temperature,
                    plan.TopP,
                    plan.ReasoningEffort,
                    plan.MaximumMessagesPerRequest,
                    plan.MaximumRequestCharacters,
                    plan.MaximumRequestImageOctets,
                    plan.RequestTimeout)
                : plan);
    }
}
