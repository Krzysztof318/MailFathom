// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class MailFolderEntity
{
    public long Id { get; set; }

    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the owner whose account this folder belongs to.</summary>
    /// <remarks>
    /// Half of the foreign key onto the account rather than a value beside one: an account is identified by its owner
    /// and its identifier together, so this column and the one above it are the reference, and the cascade that erases
    /// a folder with its account runs through both. It is also what the binding index leads with, which is the read
    /// this column had before it was part of the key.
    /// </remarks>
    public required Guid OwnerId { get; set; }

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
