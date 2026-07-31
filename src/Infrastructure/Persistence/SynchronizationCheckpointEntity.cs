// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence;

[RequiresIntegrationCoverage]
internal sealed class SynchronizationCheckpointEntity
{
    public long MailFolderId { get; set; }

    public required MailFolderEntity MailFolder { get; set; }

    public uint UidValidity { get; set; }

    public uint? LastSeenUid { get; set; }

    public DateTimeOffset? SynchronizedAt { get; set; }

    public uint ConcurrencyVersion { get; set; }
}
