// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>Composes the two statements that maintain one profile's approximate vector index.</summary>
/// <remarks>
/// <para>
/// <b>These statements are composed rather than parameterized, and that is not a choice.</b> PostgreSQL accepts no
/// parameters in a utility statement, so an index name, a width, an operator class, and a predicate value all have to
/// be part of the text. What makes that safe is where each of them comes from: the width and the metric are columns of
/// a registered profile, whose identity was validated before the row was written and is immutable afterwards; the
/// identifier is a <see cref="Guid" />; and the operator class is chosen by a closed mapping over an enum. Nothing a
/// caller types reaches the text, which is why the profile — and never a declaration, a configuration value, or a
/// request — is what these methods take.
/// </para>
/// <para>
/// Composition lives here, apart from the statements' execution, so exactly that property is provable without a
/// database: a test can hand this a profile whose provider and model names are hostile and read the whole statement
/// back.
/// </para>
/// </remarks>
internal static class EmbeddingVectorIndexStatements
{
    /// <summary>The table every one of these statements is about.</summary>
    private const string Table = "email_embeddings";

    /// <summary>The dimensionless column the expression index casts to a width.</summary>
    private const string VectorColumn = "Embedding";

    /// <summary>The column whose value restricts the partial index to one profile.</summary>
    private const string ProfileColumn = "EmbeddingProfileId";

    /// <summary>What every index name here begins with, before the profile that owns it.</summary>
    /// <remarks>
    /// Twenty-five characters, followed by a UUID written as thirty-two hexadecimal digits, is fifty-seven — inside the
    /// sixty-three bytes PostgreSQL keeps of an identifier before truncating it. Truncation would be the worst
    /// available failure here, because two profiles whose names collided after it would look to
    /// <c>CREATE INDEX IF NOT EXISTS</c> like one index that already exists.
    /// </remarks>
    private const string IndexNamePrefix = "ix_email_embeddings_hnsw_";

    /// <summary>Names the approximate index belonging to one profile.</summary>
    /// <param name="profileId">The profile that owns the index.</param>
    /// <returns>The index name, derived from the identifier alone.</returns>
    /// <remarks>
    /// Derived rather than stored, so removal needs nothing about a profile but its identifier, and so no second record
    /// can disagree with the index that actually exists.
    /// </remarks>
    internal static string IndexNameFor(EmbeddingProfileId profileId) =>
        IndexNamePrefix + profileId.Value.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>Composes the statement that builds one profile's approximate index.</summary>
    /// <param name="profile">The profile whose geometry decides the index's width and operator class.</param>
    /// <returns>A <c>CREATE INDEX</c> statement, idempotent in the profile it is for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the profile records a metric pgvector has no operator class for.</exception>
    internal static string CreateIndexFor(RegisteredEmbeddingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var dimension = profile.Identity.Dimension.ToString(CultureInfo.InvariantCulture);
        var operatorClass = OperatorClassFor(profile.Identity.DistanceMetric);

        // The cast to a width is what lets one dimensionless column hold several generations, and the predicate is what
        // keeps this index reading only the rows of the width it was built for. `IF NOT EXISTS` carries the caveat that
        // PostgreSQL does not compare the existing index with the one asked for — which is exactly why the name is
        // derived from an immutable identity, so a name already taken is taken by this same index.
        return $"""
                CREATE INDEX IF NOT EXISTS "{IndexNameFor(profile.Id)}"
                ON {Table} USING hnsw (("{VectorColumn}"::vector({dimension})) {operatorClass})
                WHERE "{ProfileColumn}" = '{Identifier(profile.Id)}'::uuid
                """;
    }

    /// <summary>Composes the statement that removes one profile's approximate index.</summary>
    /// <param name="profileId">The profile whose index is to go.</param>
    /// <returns>A <c>DROP INDEX</c> statement that succeeds whether or not the index is there.</returns>
    internal static string DropIndexFor(EmbeddingProfileId profileId) =>
        $"""
         DROP INDEX IF EXISTS "{IndexNameFor(profileId)}"
         """;

    /// <summary>Chooses the operator class that measures distance the way the profile's space does.</summary>
    /// <remarks>
    /// A closed mapping over the metric rather than a name carried in the profile: an operator class is pgvector's
    /// vocabulary rather than MailFathom's, and one taken from a record would be a string reaching a statement no
    /// parameter can protect. Indexing under the wrong one returns a number instead of an error, which is why an
    /// unmapped metric is refused rather than defaulted.
    /// </remarks>
    private static string OperatorClassFor(EmbeddingDistanceMetric distanceMetric) => distanceMetric switch
    {
        EmbeddingDistanceMetric.Cosine => "vector_cosine_ops",
        EmbeddingDistanceMetric.InnerProduct => "vector_ip_ops",
        EmbeddingDistanceMetric.EuclideanDistance => "vector_l2_ops",
        _ => throw new ArgumentOutOfRangeException(
            nameof(distanceMetric),
            distanceMetric,
            "The distance metric has no pgvector operator class."),
    };

    /// <summary>Writes a profile identifier as the hyphenated hexadecimal literal PostgreSQL reads as a UUID.</summary>
    private static string Identifier(EmbeddingProfileId profileId) =>
        profileId.Value.ToString("D", CultureInfo.InvariantCulture);
}
