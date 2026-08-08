// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Retrieval;
using MailFathom.Application.Retrieval;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Turns the bound relevance-filter declaration into the plan the second retrieval pass runs on.</summary>
/// <remarks>
/// The mapping is separate from the options type for the reason every mapper in this directory is: the bound object is
/// mutable, binder-shaped, and carries defaults that mean "nothing was written", while the plan is the validated value
/// the filter is allowed to assume.
/// </remarks>
internal static class PassageRelevanceFilterPlanMapper
{
    /// <summary>Builds the plan a declared relevance filter describes.</summary>
    /// <param name="settings">The bound chat declaration, already validated.</param>
    /// <param name="retrievalBounds">What one retrieval hands over, which is both the ceiling on the candidate count and its default.</param>
    /// <returns>The plan, or <see langword="null" /> when this deployment declared no chat provider or left the pass off.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Nothing declared is not a failure, and neither is declaring a chat endpoint without this. Both return nothing, so
    /// the composition root registers no filter rather than one that would have to be asked on every lookup whether it
    /// was meant to run. A candidate count nobody wrote resolves to everything the retrieval hands over, which is where
    /// that default belongs: it is the only place both numbers are known at once.
    /// </remarks>
    public static PassageRelevanceFilterPlan? Map(ChatModelOptions settings, EmailKnowledgeBounds retrievalBounds)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(retrievalBounds);

        return settings.IsConfigured && settings.RelevanceFilter.Enabled
            ? PassageRelevanceFilterPlan.Create(
                retrievalBounds,
                settings.RelevanceFilter.MaxCandidates ?? retrievalBounds.MaximumPassages,
                settings.RelevanceFilter.MinimumRelevance)
            : null;
    }
}
