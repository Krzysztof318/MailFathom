// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Sources;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One secret MailFathom stores sealed behind a database reference.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class StoredSecretEntity
{
    private const int AesGcmEnvelopeHeaderByteCount = 28;

    internal const int MaximumKeyIdLength = 64;

    internal const int MaximumSealedMaterialByteCount =
        SecretMaterialLimits.MaximumMaterialByteCount + AesGcmEnvelopeHeaderByteCount;

    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public required string Name { get; set; }

    public required byte[] SealedMaterial { get; set; }

    public required string DataEncryptionKeyId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public OwnerAccountEntity Owner { get; set; } = null!;

    internal static int MinimumSealedMaterialByteCount => AesGcmEnvelopeHeaderByteCount + 1;

    internal static int MaximumNameLength => SecretName.MaximumLength;
}
