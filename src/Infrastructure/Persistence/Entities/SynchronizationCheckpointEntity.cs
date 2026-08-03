// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class SynchronizationCheckpointEntity
{
    public long MailFolderId { get; set; }

    public required MailFolderEntity MailFolder { get; set; }

    public uint UidValidity { get; set; }

    public uint? LastSeenUid { get; set; }

    public DateTimeOffset? SynchronizedAt { get; set; }

    /// <summary>Gets or sets the folder modification sequence the backward pass has covered the whole folder through.</summary>
    /// <remarks>
    /// It is a signed column because RFC 7162 bounds a modification sequence to 63 bits, so every value a server may
    /// report fits one and PostgreSQL keeps an ordered type it can index. A row written before this column existed
    /// reads as absent, which is exactly what a folder nobody has reconciled by sequence means.
    /// </remarks>
    public long? ReconciledThroughModSeq { get; set; }

    public uint ConcurrencyVersion { get; set; }
}
