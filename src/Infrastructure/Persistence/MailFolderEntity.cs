// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.CodeCoverage;

namespace MailMcp.Infrastructure.Persistence;

[RequiresIntegrationCoverage]
internal sealed class MailFolderEntity
{
    public long Id { get; set; }

    public required string MailboxAccountId { get; set; }

    public required string RemoteName { get; set; }

    public required MailboxAccountEntity MailboxAccount { get; set; }

    public ICollection<StoredEmailEntity> StoredEmails { get; } = [];

    public SynchronizationCheckpointEntity? SynchronizationCheckpoint { get; set; }
}
