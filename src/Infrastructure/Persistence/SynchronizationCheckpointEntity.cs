// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;

namespace MailMcp.Infrastructure.Persistence;

// TODO: Remove this exclusion when the planned PostgreSQL integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by PostgreSQL integration tests.")]
internal sealed class SynchronizationCheckpointEntity
{
    public long MailFolderId { get; set; }

    public required MailFolderEntity MailFolder { get; set; }

    public uint UidValidity { get; set; }

    public uint? LastSeenUid { get; set; }

    public DateTimeOffset? SynchronizedAt { get; set; }

    public uint ConcurrencyVersion { get; set; }
}
