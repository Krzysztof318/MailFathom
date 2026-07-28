// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.CodeCoverage;

namespace MailMcp.Infrastructure.Persistence;

[RequiresIntegrationCoverage]
internal sealed class MailFolderEntity
{
    public long Id { get; set; }

    public required string MailboxAccountId { get; set; }

    public required string Alias { get; set; }

    public int ResolutionGeneration { get; set; }

    public required string RemotePath { get; set; }

    // Stored as text rather than as a single character, because PostgreSQL pads `character(1)` and the provider
    // mapping of a nullable CLR `char` has not been validated against a real database yet.
    public string? HierarchyDelimiter { get; set; }

    public required MailboxAccountEntity MailboxAccount { get; set; }

    public ICollection<StoredEmailEntity> StoredEmails { get; } = [];

    public SynchronizationCheckpointEntity? SynchronizationCheckpoint { get; set; }
}
