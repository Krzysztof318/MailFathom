// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One vector space MailFathom has embedded into, and where that generation is in its life.</summary>
/// <remarks>
/// <para>
/// The row exists so a stored vector stays attributable to exactly what produced it, long after the configuration that
/// declared the model was edited. Configuration is the declarative half — it says what a deployment intends to embed
/// with — and this row is written by the activation that took that declaration up and began spending against it.
/// </para>
/// <para>
/// It holds two classes of column and nothing else. The identity is fixed at insertion and covered by
/// <see cref="IdentityFingerprint" />, so a vector's attribution can never be edited out from under it; the lifecycle
/// columns are the only thing that moves. Everything operational — the endpoint address, the credential, the batch
/// size, the request rate, the concurrency, the ceilings — is configuration and reaches no column here, which is what
/// makes rotating a key or raising a rate limit an edit rather than a re-embed. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>.
/// </para>
/// <para>
/// Nothing here is personal data or a secret. A profile describes a model, and the credential that reaches it is
/// configuration by the same decision.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmbeddingProfileEntity
{
    public Guid Id { get; set; }

    /// <summary>Gets or sets the vendor whose model defines this space, rather than the endpoint it is reached through.</summary>
    public required string Provider { get; set; }

    /// <summary>Gets or sets the model identifier the provider publishes.</summary>
    public required string ModelIdentifier { get; set; }

    /// <summary>Gets or sets the model version the provider exposes, or <see langword="null" /> where it exposes none.</summary>
    public string? ModelVersion { get; set; }

    /// <summary>Gets or sets the width of the vectors written under this profile.</summary>
    /// <remarks>
    /// The width the stored vectors actually have, never the model's nominal one. Where a model was narrowed to what the
    /// database can index, the narrowed number is what belongs here, because a trimmed vector occupies a different space
    /// than the full one. The value is half of the alternate key <see cref="EmailEmbeddingEntity" /> points at, which is
    /// what lets PostgreSQL rather than the writing code refuse a vector of the wrong length.
    /// </remarks>
    public int Dimension { get; set; }

    /// <summary>Gets or sets how distance is measured between two vectors of this space.</summary>
    public EmbeddingDistanceMetric DistanceMetric { get; set; }

    /// <summary>Gets or sets the width a passage is cut to before it is sent.</summary>
    public int InputCharacterLimit { get; set; }

    /// <summary>Gets or sets the instruction a model requires of a passage, or <see langword="null" /> where it requires none.</summary>
    public string? PassageInstruction { get; set; }

    /// <summary>Gets or sets whether the stored vectors were normalized to unit length.</summary>
    public bool NormalizesVector { get; set; }

    /// <summary>Gets or sets the digest over every identity column, which the table is unique on.</summary>
    /// <remarks>
    /// The unique index over this is what makes activation idempotent: a declaration whose geometry is already
    /// registered resolves to this row instead of inserting a second one that would be re-embedded for nothing.
    /// </remarks>
    public required string IdentityFingerprint { get; set; }

    /// <summary>Gets or sets where this profile is in its life.</summary>
    public EmbeddingProfileLifecycleState LifecycleState { get; set; }

    /// <summary>Gets or sets when the profile was registered, which is when its identity became permanent.</summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>Gets or sets when this profile last became the one retrieval reads, or <see langword="null" /> while it never has.</summary>
    /// <remarks>
    /// Null is a generation still being built rather than an unrecorded moment, and a profile activated again after a
    /// rollback carries the later activation: what a reader asks of this column is when the vectors now being served
    /// started being served.
    /// </remarks>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>Gets or sets when a later generation replaced this one, or <see langword="null" /> while none has.</summary>
    public DateTimeOffset? SupersededAt { get; set; }

    public ICollection<EmailEmbeddingEntity> Embeddings { get; } = [];
}
