// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Mutations;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class MailboxMutationEntity
{
    /// <summary>The longest remote folder path stored, matching the bound the folder binding's own path column carries.</summary>
    internal const int MaximumDestinationPathLength = 512;

    public Guid Id { get; set; }

    public Guid StoredEmailId { get; set; }

    public required StoredEmailEntity StoredEmail { get; set; }

    /// <summary>
    /// Gets or sets the account the source folder belongs to, copied from that folder because the query that lists an
    /// account's unfinished mutations leads with it and an index cannot span a join. It is written once with the row for
    /// the same reason the stored email's copy is: nothing repoints a folder at another account.
    /// </summary>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the alias binding the email was in when the change was asked for.</summary>
    /// <remarks>
    /// The source occurrence is kept beside <see cref="StoredEmailId" /> rather than read from the email, because the
    /// email moves and this record says where the change was aimed. A relocation that succeeds leaves the stored email
    /// in another folder, and a record that followed it there would no longer describe the command that was issued.
    /// </remarks>
    public long MailFolderId { get; set; }

    public required MailFolderEntity MailFolder { get; set; }

    public uint UidValidity { get; set; }

    public uint Uid { get; set; }

    /// <summary>Gets or sets the mutation's own name, which is the identity the closed enumeration publishes.</summary>
    /// <remarks>
    /// The column holds the name directly rather than a converted enum, because the name is what the value object is:
    /// it is what a log line records, what a span is called, and what a counter is broken down by, so the stored form is
    /// the same word an operator has already read everywhere else.
    /// </remarks>
    public required string Mutation { get; set; }

    public MailboxMutationOrigin RequesterOrigin { get; set; }

    public required string RequesterIdentity { get; set; }

    /// <summary>Gets or sets the folder a relocation or a copy names, and <see langword="null" /> for every other mutation.</summary>
    public string? DestinationFolderPath { get; set; }

    /// <summary>Gets or sets the hierarchy delimiter the destination path was created with, where one is known.</summary>
    public string? DestinationHierarchyDelimiter { get; set; }

    /// <summary>Gets or sets which way a <c>\Seen</c> change was asked for, and <see langword="null" /> for every other mutation.</summary>
    public bool? DesiredSeenState { get; set; }

    public MailboxMutationStage Stage { get; set; }

    /// <summary>Gets or sets the UIDVALIDITY a <c>COPYUID</c> response named, where the server supplied one.</summary>
    public uint? PlacementUidValidity { get; set; }

    /// <summary>Gets or sets the UID a <c>COPYUID</c> response named, where the server supplied one.</summary>
    public uint? PlacementUid { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public DateTimeOffset StageChangedAt { get; set; }

    /// <summary>Gets or sets the code of the failure the last attempt ended in, and <see langword="null" /> while none has.</summary>
    /// <remarks>
    /// Only the code is kept. A failure message is text assembled at the failure site, and this record is read by an
    /// operator asking which changes are stuck rather than by anybody re-reading a log line.
    /// </remarks>
    public int? LastFailureCode { get; set; }

    /// <summary>Gets or sets the PostgreSQL <c>xmin</c> token this row's optimistic concurrency is detected through.</summary>
    /// <remarks>See the stored-email mapping: this is the system column, not a user-defined one.</remarks>
    public uint ConcurrencyVersion { get; set; }
}
