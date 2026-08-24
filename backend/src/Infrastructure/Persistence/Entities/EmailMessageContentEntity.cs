// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class EmailMessageContentEntity
{
    public Guid StoredEmailId { get; set; }

    /// <summary>Gets or sets which store holds this payload, which is the authority for this row and nothing else.</summary>
    /// <remarks>
    /// <para>
    /// A deployment's configured backend says where the next write goes; this says where this one went. The two are
    /// unrelated for an existing row, which is what lets a deployment hold both kinds of row indefinitely and read each
    /// from wherever it was written.
    /// </para>
    /// <para>
    /// The column carries a stored default naming the database, which is what makes every row written before the
    /// discriminator existed read as the thing it is. It answers an ordinary write as well, because the database is
    /// this property's own default and EF Core leaves such a value out of an insert; an object-backed row is the one
    /// that states its backend, which is also the only one whose answer the check constraint could not infer.
    /// </para>
    /// </remarks>
    public ContentStorageBackend Backend { get; set; }

    /// <summary>Gets or sets the whole key the object was written under, or <see langword="null" /> when the database holds the payload.</summary>
    /// <remarks>
    /// Stored exactly as the adapter produced it and never recomputed, because nothing about this row determines the
    /// key: it was minted before the row existed, which is what let the object be written outside the transaction that
    /// committed this row.
    /// </remarks>
    public string? ObjectLocator { get; set; }

    /// <summary>Gets or sets the payload the row itself carries, or <see langword="null" /> when the object backend holds it.</summary>
    public byte[]? RawMime { get; set; }

    public long MimeByteLength { get; set; }

    public required byte[] Sha256Hash { get; set; }

    public DateTimeOffset StoredAt { get; set; }

    public required StoredEmailEntity StoredEmail { get; set; }
}
