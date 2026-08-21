// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.AI.Embeddings;

/// <summary>Decides whether every endpoint of a fallback chain reaches the same vector space.</summary>
/// <remarks>
/// <para>
/// A fallback is another route to one vector space and never a second one, which
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// makes a startup refusal rather than a runtime surprise. The refusal is the point: a fallback on a different model
/// does not produce a degraded vector, it produces a point in a different space, and a distance computed against it is
/// a number with no meaning. Written under the active profile those vectors would corrupt retrieval in the way that is
/// hardest to attribute — slightly worse results rather than an error.
/// </para>
/// <para>
/// The report names both endpoints and the one property they differ on, because an operator reading "the chain
/// disagrees" has to diff two configuration blocks by hand to learn what this already knows.
/// </para>
/// </remarks>
public static class EmbeddingChainAgreement
{
    /// <summary>Reports the first disagreement in a chain, or nothing when every endpoint declares one geometry.</summary>
    /// <param name="endpoints">The endpoints in the order they were declared.</param>
    /// <returns>A message naming the two endpoints and the property they differ on, or <see langword="null" /> when they agree.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Every endpoint is compared against the first rather than against its predecessor, so the message always names
    /// the declaration the chain is measured by instead of whichever neighbour happened to differ.
    /// </remarks>
    public static string? FindDisagreement(IReadOnlyList<EmbeddingEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        if (endpoints.Count < 2)
        {
            return null;
        }

        var first = endpoints[0];

        return endpoints
            .Skip(1)
            .Select(candidate => DescribeDifference(first, candidate))
            .FirstOrDefault(difference => difference is not null);
    }

    private static string? DescribeDifference(EmbeddingEndpoint first, EmbeddingEndpoint candidate)
    {
        var expected = first.Identity;
        var actual = candidate.Identity;

        if (!string.Equals(expected.Provider, actual.Provider, StringComparison.Ordinal))
        {
            return Describe(first, candidate, "provider", expected.Provider, actual.Provider);
        }

        if (!string.Equals(expected.ModelIdentifier, actual.ModelIdentifier, StringComparison.Ordinal))
        {
            return Describe(first, candidate, "model", expected.ModelIdentifier, actual.ModelIdentifier);
        }

        if (!string.Equals(expected.ModelVersion, actual.ModelVersion, StringComparison.Ordinal))
        {
            return Describe(first, candidate, "model version", DescribeOptional(expected.ModelVersion), DescribeOptional(actual.ModelVersion));
        }

        if (expected.Dimension != actual.Dimension)
        {
            return Describe(first, candidate, "dimension", expected.Dimension, actual.Dimension);
        }

        if (expected.DistanceMetric != actual.DistanceMetric)
        {
            return Describe(first, candidate, "distance metric", expected.DistanceMetric, actual.DistanceMetric);
        }

        return DescribePreparationDifference(first, candidate);
    }

    private static string? DescribePreparationDifference(EmbeddingEndpoint first, EmbeddingEndpoint candidate)
    {
        var expected = first.Identity.InputPreparation;
        var actual = candidate.Identity.InputPreparation;

        if (expected.InputCharacterLimit != actual.InputCharacterLimit)
        {
            return Describe(first, candidate, "input character limit", expected.InputCharacterLimit, actual.InputCharacterLimit);
        }

        // The instruction is compared rather than reported. It is an operator-written prompt fragment, and a chain that
        // differs on it is corrected by reading the two blocks, not by having both of them repeated into a log.
        if (!string.Equals(expected.PassageInstruction, actual.PassageInstruction, StringComparison.Ordinal))
        {
            return $"Embedding endpoints '{first.Alias}' and '{candidate.Alias}' declare different passage instructions, "
                + "so they are two vector spaces rather than two routes to one.";
        }

        return expected.NormalizesVector == actual.NormalizesVector
            ? null
            : Describe(first, candidate, "vector normalization", expected.NormalizesVector, actual.NormalizesVector);
    }

    private static string Describe<TValue>(
        EmbeddingEndpoint first,
        EmbeddingEndpoint candidate,
        string property,
        TValue expected,
        TValue actual) =>
        $"Embedding endpoints '{first.Alias}' and '{candidate.Alias}' differ on {property}: "
        + $"'{expected}' against '{actual}'. Every endpoint of one chain reaches the same vector space, "
        + "so a different model is a second profile and a deliberate switch rather than a fallback.";

    private static string DescribeOptional(string? value) => value ?? "(none)";
}
