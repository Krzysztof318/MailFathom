// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Indexing;

/// <summary>Maintains the one approximate index through which a profile's vectors are searched.</summary>
/// <remarks>
/// <para>
/// The index belongs to a profile rather than to the table, which is a consequence of the dimensionless vector column:
/// one index covers one width, so a table holding two generations of different widths cannot be served by a single one.
/// That is why no migration creates it and why building it is an administrative act — activating a profile is what
/// calls for one, and removing or superseding that profile is what calls for it to go. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>.
/// </para>
/// <para>
/// Nothing here decides <em>when</em> to activate, and neither operation changes an answer. Before an approximate index
/// exists, a vector search is exact: correct, and linear in the number of vectors. That is what makes both operations
/// safe to perform after the fact and what keeps a failure to build one a performance finding rather than a wrong
/// result.
/// </para>
/// </remarks>
public interface IEmbeddingProfileVectorIndex
{
    /// <summary>Builds the approximate index for one profile, leaving an existing one in place.</summary>
    /// <param name="profile">The profile whose vectors the index serves, and whose geometry decides its shape.</param>
    /// <param name="cancellationToken">Cancels the build.</param>
    /// <returns>A task that completes when the profile has its index.</returns>
    /// <remarks>
    /// Idempotent, so activating a profile that already has its index builds nothing and does not fail. A profile's
    /// identity is immutable and the index is named after that identity, so an index already carrying the name is the
    /// index this call would have built rather than a differently shaped one wearing the same name.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile" /> is <see langword="null" />.</exception>
    /// <exception cref="EmbeddingVectorIndexFailedException">Thrown when the database refused to build the index.</exception>
    Task EnsureBuiltAsync(RegisteredEmbeddingProfile profile, CancellationToken cancellationToken);

    /// <summary>Removes the approximate index belonging to one profile, if it has one.</summary>
    /// <param name="profileId">The profile whose index is to go.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the profile has no index.</returns>
    /// <remarks>
    /// Takes the identifier alone rather than the profile, because a superseded generation is removed by a caller that
    /// holds nothing else about it, and the index is named after the identifier. Removing an index a profile never had
    /// is not a failure.
    /// </remarks>
    /// <exception cref="EmbeddingVectorIndexFailedException">Thrown when the database refused to remove the index.</exception>
    Task RemoveAsync(EmbeddingProfileId profileId, CancellationToken cancellationToken);
}
