// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Sources;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One secret MailFathom stores sealed behind a database reference.</summary>
/// <remarks>
/// The row carries no concurrency token because replacing material is a last-writer rotation over the stable
/// owner-and-name identity. Concurrent first writes converge through that identity's unique constraint and a
/// fresh-session retry rather than by rejecting a later version.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class StoredSecretEntity
{
    /// <summary>The nonce and authentication-tag bytes AES-GCM places before every ciphertext.</summary>
    /// <remarks><see cref="Common.AesGcmEnvelope" /> fixes the envelope as a 12-byte nonce followed by a 16-byte tag and the ciphertext.</remarks>
    private const int AesGcmEnvelopeHeaderByteCount = 28;

    /// <summary>The longest data-encryption key identifier accepted by configuration and stored beside a value.</summary>
    internal const int MaximumKeyIdLength = 64;

    /// <summary>The largest sealed value: the ordinary secret-material bound plus the fixed AES-GCM envelope.</summary>
    internal const int MaximumSealedMaterialByteCount =
        SecretMaterialLimits.MaximumMaterialByteCount + AesGcmEnvelopeHeaderByteCount;

    /// <summary>The stable identifier carried by a <c>database:</c> reference.</summary>
    public Guid Id { get; set; }

    /// <summary>The owner whose erasure cascades to this material.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>The safe name used as the rotation identity and authenticated into the ciphertext.</summary>
    public required string Name { get; set; }

    /// <summary>The nonce, authentication tag, and ciphertext produced by the data-encryption key.</summary>
    public required byte[] SealedMaterial { get; set; }

    /// <summary>The key-ring identifier needed to open this sealed value and inventory key retirement.</summary>
    public required string DataEncryptionKeyId { get; set; }

    /// <summary>When this owner-and-name identity was first stored.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When its material was last replaced or re-sealed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The owner row that gives this material its lifetime.</summary>
    public OwnerAccountEntity Owner { get; set; } = null!;

    /// <summary>The smallest valid sealed value: the fixed envelope and at least one material byte.</summary>
    internal static int MinimumSealedMaterialByteCount => AesGcmEnvelopeHeaderByteCount + 1;

    /// <summary>The longest stored name, kept identical to the secret-name grammar.</summary>
    internal static int MaximumNameLength => SecretName.MaximumLength;
}
