// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
namespace MailMcp.Infrastructure.Persistence;

// TODO: Remove this exclusion when the planned PostgreSQL integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by PostgreSQL integration tests.")]
internal sealed class MailFolderEntity
{
    public long Id { get; set; }

    public required string MailboxAccountId { get; set; }

    public required string RemoteName { get; set; }

    public required MailboxAccountEntity MailboxAccount { get; set; }

    public ICollection<StoredEmailEntity> StoredEmails { get; } = [];

    public SynchronizationCheckpointEntity? SynchronizationCheckpoint { get; set; }
}
