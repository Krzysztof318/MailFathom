// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
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

    /// <summary>Gets or sets the owner whose account the source folder belongs to.</summary>
    public required Guid OwnerId { get; set; }

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

    /// <summary>Gets or sets which way a <c>\Flagged</c> change was asked for, and <see langword="null" /> for every other mutation.</summary>
    public bool? DesiredFlaggedState { get; set; }

    /// <summary>Gets or sets the keywords a keyword mutation names, and <see langword="null" /> for every other mutation.</summary>
    /// <remarks>
    /// <para>
    /// A null column and an empty array mean different things here, which is why the column is nullable rather than
    /// defaulting to an empty array. Null says the mutation is not one that names keywords at all; an empty array says
    /// a replacement was asked for and names none, which is a request to clear every keyword the message carries.
    /// </para>
    /// <para>
    /// The keywords are stored as they were written rather than folded, for the reason the domain value keeps them that
    /// way: this is what a <c>STORE</c> will put on somebody's message, and a resumed attempt has to issue the same
    /// command the first one would have.
    /// </para>
    /// </remarks>
    public string[]? Keywords { get; set; }

    /// <summary>Gets or sets what becomes of the local copy after a delete, and <see langword="null" /> for every other mutation.</summary>
    /// <remarks>
    /// It is written once with the row and never rewritten, which is the whole reason it is stored rather than read
    /// where the delete finishes. A delete completes locally in a later synchronization run, so reading the account's
    /// configuration there would apply whatever the operator had changed it to in the meantime to a deletion that was
    /// authored under the previous answer.
    /// </remarks>
    public AuthoredDeleteEmailDisposition? LocalDisposition { get; set; }

    /// <summary>Gets or sets whether this mutation leaves an entry in the account's audit trail when it ends.</summary>
    /// <remarks>
    /// Resolved from the account's configuration when the row is written and never rewritten, which is the whole reason
    /// it is stored rather than read where the mutation ends. A mutation ends in a later run — sometimes days later —
    /// and reading the setting there would apply whatever the operator had changed it to in the meantime, producing a
    /// history whose gaps look like changes that never happened.
    /// </remarks>
    public bool AuditTrailEnabled { get; set; }

    public MailboxMutationStage Stage { get; set; }

    /// <summary>Gets or sets whether the placement left a source occurrence that still has to be removed separately.</summary>
    /// <remarks>
    /// Written with the <see cref="MailboxMutationStage.PlacementIssued" /> stage and read by a resumed attempt, because
    /// it is the one thing about a half-finished relocation that cannot be worked out afterwards: <c>MOVE</c> removes
    /// the source itself and a copy does not, so the same stage means opposite things depending on which ran. Asking the
    /// connection instead would let a fallback relocation resumed against a server now advertising <c>MOVE</c> be read
    /// as finished, leaving the email in both folders permanently.
    /// </remarks>
    public bool RequiresSourceRemoval { get; set; }

    /// <summary>Gets or sets the UIDVALIDITY a <c>COPYUID</c> response named, where the server supplied one.</summary>
    public uint? PlacementUidValidity { get; set; }

    /// <summary>Gets or sets the UID a <c>COPYUID</c> response named, where the server supplied one.</summary>
    public uint? PlacementUid { get; set; }

    /// <summary>Gets or sets when synchronization recognized the occurrence this mutation created, and <see langword="null" /> while it has not.</summary>
    /// <remarks>
    /// It is a separate fact from <see cref="Stage" /> because the two are recorded by different runs from different
    /// answers. The stage says what the server acknowledged when the command was issued; this says that an ordinary
    /// synchronization run has since discovered the message in the destination folder and joined it to the email it
    /// already had.
    /// </remarks>
    public DateTimeOffset? PlacementObservedAt { get; set; }

    /// <summary>Gets or sets when synchronization saw the source occurrence leave its folder, and <see langword="null" /> while it has not.</summary>
    public DateTimeOffset? SourceRemovalObservedAt { get; set; }

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
