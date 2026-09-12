// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.BodyCleanup;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Turns the bound declaration into the plan the body-cleanup pass runs on.</summary>
/// <remarks>
/// The mapping is separate from the options type for the reason every mapper in this directory is: the bound object is
/// mutable, binder-shaped, and carries empty strings that mean absence, while the plan is the validated value the pass is
/// allowed to assume. What this one adds over <see cref="ChatGenerationPlanMapper" /> is which reference it reads — the
/// block's own, which is the whole reason that block exists.
/// </remarks>
internal static class MailBodyCleanupPlanMapper
{
    /// <summary>Builds the plan a declared body-cleanup pass describes.</summary>
    /// <param name="settings">The bound chat declaration, already validated.</param>
    /// <returns>The plan, or <see langword="null" /> when this deployment declared no chat provider.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A reference nobody wrote resolves to the deployment's main model, which is what keeps the common declaration — one
    /// model and nothing else — from needing a key to say "the same model as everything else".
    /// </remarks>
    public static MailBodyCleanupPlan? Map(ChatModelOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return ChatGenerationPlanMapper.Map(settings, settings.BodyCleanup.Model) is { } plan
            ? new MailBodyCleanupPlan(plan)
            : null;
    }
}
