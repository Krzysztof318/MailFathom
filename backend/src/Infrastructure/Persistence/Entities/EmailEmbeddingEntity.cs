// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using Pgvector;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One passage of mail as a point in one vector space.</summary>
/// <remarks>
/// <para>
/// The key is the chunk and the profile together, because that pair is what a vector is: re-embedding a passage under
/// the profile already serving it replaces the row rather than adding one, and the constraint is what an idempotent
/// upsert conflicts on. A surrogate key would leave that uniqueness to a second index and give an upsert nothing to
/// name. Nothing references a vector row, so it needs no identifier of its own.
/// </para>
/// <para>
/// The column is pgvector's dimensionless <c>vector</c> rather than <c>vector(N)</c>, which is what lets two profiles of
/// different widths share one table and each get an expression index of its own when it is activated. Dropping the
/// width from the column does not drop it from the schema: <see cref="Dimension" /> travels with the profile reference
/// as a composite foreign key onto the profile's own dimension, and a check constraint ties it to the stored vector's
/// actual length. A provider returning a vector of an unexpected width therefore fails at the write instead of
/// corrupting a search.
/// </para>
/// <para>
/// A vector is derived from mail content and inherits the source message's classification, retention, export, and
/// erasure obligations whole. The cascade from the chunk is what keeps deleting a message a deletion of everything
/// derived from it; nothing about being a number makes a vector a lesser copy of the words it stands for.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailEmbeddingEntity
{
    public Guid EmailChunkId { get; set; }

    /// <summary>Gets or sets the passage this vector stands for, once something has loaded it.</summary>
    /// <remarks>
    /// Optional on the entity although the relationship is mandatory in the schema, and the same holds for
    /// <see cref="EmbeddingProfile" />. A writer produces vectors from chunk identifiers a query returned and the
    /// identifier of the profile it is embedding under, so requiring the navigations would make it materialize two
    /// principals per vector — and assigning either would let EF's key fixup overwrite <see cref="Dimension" /> with the
    /// principal's, which is the one value here that has to be written rather than inherited.
    /// </remarks>
    public EmailChunkEntity? EmailChunk { get; set; }

    public Guid EmbeddingProfileId { get; set; }

    /// <summary>Gets or sets the vector space this point belongs to, once something has loaded it.</summary>
    public EmbeddingProfileEntity? EmbeddingProfile { get; set; }

    /// <summary>Gets or sets the width of the stored vector.</summary>
    /// <remarks>
    /// Carried on the row rather than read from the profile, because a PostgreSQL check constraint sees only its own
    /// row. Writing it here and pointing the profile foreign key at <c>(Id, Dimension)</c> is what makes the check
    /// against the profile's dimension expressible at all: the foreign key refuses a width the profile does not declare,
    /// and the check refuses a vector whose length disagrees with the width beside it.
    /// </remarks>
    public int Dimension { get; set; }

    /// <summary>Gets or sets the passage's position in the profile's vector space.</summary>
    public required Vector Embedding { get; set; }

    /// <summary>Gets or sets when the vector was produced, which tells a re-embed from an original one apart.</summary>
    public DateTimeOffset GeneratedAt { get; set; }
}
