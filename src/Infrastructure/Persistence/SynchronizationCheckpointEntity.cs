// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.CodeCoverage;

namespace MailMcp.Infrastructure.Persistence;

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
