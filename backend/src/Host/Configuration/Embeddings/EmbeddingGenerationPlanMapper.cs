// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;

namespace MailFathom.Host.Configuration.Embeddings;

/// <summary>Turns the bound embedding declaration into the plan the provider adapter runs on.</summary>
/// <remarks>
/// The mapping is separate from the options type for the reason every mapper in this directory is: the bound object is
/// mutable, binder-shaped, and full of empty strings that mean absence, while the plan is the validated value the
/// adapter is allowed to assume. Keeping the two apart is what lets the adapter hold no defaulting logic at all.
/// </remarks>
internal static class EmbeddingGenerationPlanMapper
{
    /// <summary>Builds the plan a declared chain describes.</summary>
    /// <param name="settings">The bound declaration, already validated.</param>
    /// <returns>The plan, or <see langword="null" /> when the deployment declared no embedding provider.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Nothing declared is not a failure. An instance that has not chosen a provider serves lexical search exactly as
    /// it did before, and returning nothing is what lets the composition root register no generator rather than one
    /// that fails at first use.
    /// </remarks>
    public static EmbeddingGenerationPlan? Map(EmbeddingOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.IsConfigured
            ? EmbeddingGenerationPlan.Create(
                [.. settings.Endpoints.Select(endpoint => endpoint.ToEndpoint())],
                settings.AllowTrimVectors,
                settings.MaxPassagesPerRequest,
                settings.RequestTimeout)
            : null;
    }
}
