// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class StoredEmailEntity
{
    /// <summary>The longest forward path RFC 5321 accepts, which bounds every address column and every address in an array.</summary>
    internal const int MaximumAddressLength = 320;

    /// <summary>The longest header line RFC 5322 accepts, which bounds every stored message identifier.</summary>
    internal const int MaximumIdentifierLength = 998;

    /// <summary>
    /// The greatest number of addresses one header contributes to its array column. A message addressed to more
    /// mailboxes than this is a list expansion whose members no filter asks about individually, and the column exists
    /// to be filtered on rather than to be a second copy of the header.
    /// </summary>
    internal const int MaximumAddressesPerRole = 256;

    /// <summary>The greatest number of ancestors the thread column keeps, counted from the nearest.</summary>
    internal const int MaximumThreadReferences = 64;

    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the account the owning folder belongs to, copied from that folder because the account timeline
    /// index leads with it and an index cannot span a join. The folder stays the single writer of the association;
    /// nothing repoints a folder at another account, so the copy is written once with the row and never revised.
    /// </summary>
    public required string MailboxAccountId { get; set; }

    public long MailFolderId { get; set; }

    public required MailFolderEntity MailFolder { get; set; }

    public uint UidValidity { get; set; }

    public uint Uid { get; set; }

    public string? InternetMessageId { get; set; }

    public string? Subject { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Gets or sets when the last receiving hop recorded the message, which is the timeline's ordering column.</summary>
    public DateTimeOffset? ReceivedAt { get; set; }

    public long SizeOctets { get; set; }

    public StoredEmailContentAvailability ContentAvailability { get; set; }

    public string? SenderDisplayName { get; set; }

    public string? SenderAddress { get; set; }

    /// <summary>Gets or sets the comparison form of <see cref="SenderAddress" />, which is the form every filter matches on.</summary>
    public string? SenderNormalizedAddress { get; set; }

    /// <summary>Gets or sets the normalized <c>To</c> addresses, which recipient filters test for containment.</summary>
    /// <remarks>
    /// Only the comparison form is kept, for the reason the per-attachment list is not kept at all: a display name is
    /// mail content that no planned query filters or sorts on, and a second copy of it would widen the access, export,
    /// and erasure surface. A reader that needs the names re-derives them from the stored raw MIME.
    /// </remarks>
    public string[] ToAddresses { get; set; } = [];

    /// <summary>Gets or sets the normalized <c>Cc</c> addresses.</summary>
    public string[] CcAddresses { get; set; } = [];

    /// <summary>Gets or sets the normalized <c>Reply-To</c> addresses.</summary>
    public string[] ReplyToAddresses { get; set; } = [];

    /// <summary>Gets or sets the identifier of the message this one answers, without its angle brackets.</summary>
    public string? InReplyTo { get; set; }

    /// <summary>Gets or sets the referenced ancestors in header order, which is the path back to the conversation root.</summary>
    public string[] ThreadReferences { get; set; } = [];

    public int AttachmentCount { get; set; }

    public long AttachmentTotalSizeOctets { get; set; }

    public int InlineResourceCount { get; set; }

    public bool IsEncrypted { get; set; }

    /// <summary>Gets or sets whether a signature part is present. Nothing here has verified it, and the name says so.</summary>
    public bool CarriesUnverifiedSignature { get; set; }

    public bool ContainsUnexpandedTnefPart { get; set; }

    /// <summary>
    /// Gets or sets when the remote flags below were last read from the server, or <see langword="null" /> while they
    /// have never been read. The timestamp is what separates "the server reports none of these flags" from "nobody has
    /// looked yet", which no combination of the booleans can express on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The snapshot records server state and is never written towards the server: MailFathom reads mail read-only, so no
    /// application path turns any of these into an IMAP <c>STORE</c>. Reconciliation refreshes them one bounded window
    /// per run, and a row nobody has reached yet carries the never-observed value.
    /// </para>
    /// <para>
    /// The column is also the reconciliation queue. Windows are selected by the oldest value first with the
    /// never-observed rows leading, so writing an observation is what moves a row to the back of the queue and what
    /// makes the pass advance without a cursor of its own.
    /// </para>
    /// </remarks>
    public DateTimeOffset? RemoteFlagsObservedAt { get; set; }

    /// <summary>
    /// Gets or sets when reconciliation found this occurrence gone from its remote folder, or <see langword="null" />
    /// while the server still holds it. A row carrying a value is a tombstone that every mailbox query excludes.
    /// </summary>
    /// <remarks>
    /// This is a different statement from <see cref="IsRemotelyDeleted" />, and the two must not be read as one. That
    /// flag says the server reported <c>\Deleted</c> for a message the folder still holds and still serves; this
    /// timestamp says the folder no longer holds the message at all. An occurrence whose local copy is erased rather
    /// than tombstoned has no row to carry either.
    /// </remarks>
    public DateTimeOffset? RemoteExpungeObservedAt { get; set; }

    public bool IsRemotelySeen { get; set; }

    public bool IsRemotelyAnswered { get; set; }

    public bool IsRemotelyFlagged { get; set; }

    public bool IsRemotelyDraft { get; set; }

    public bool IsRemotelyDeleted { get; set; }

    public uint ConcurrencyVersion { get; set; }

    public EmailMessageContentEntity? Content { get; set; }

    /// <summary>
    /// Gets or sets the derived text this email contributes to lexical search, which is absent until extraction has
    /// run for it. Its absence on a row whose content is stored is what the extraction backfill selects on.
    /// </summary>
    public EmailSearchDocumentEntity? SearchDocument { get; set; }

    /// <summary>
    /// Gets or sets the outstanding request to fetch or read this email's content again, which exists only while a
    /// read has found the stored copy unusable. Its presence is what a repair run selects on.
    /// </summary>
    public EmailContentRepairRequestEntity? ContentRepairRequest { get; set; }

    /// <summary>
    /// Gets or sets the retrievable passages this email's extracted text was cut into, which are empty until chunking
    /// has run for it and stay empty for a message whose body yielded no text.
    /// </summary>
    public ICollection<EmailChunkEntity> Chunks { get; } = [];

    /// <summary>
    /// Gets the changes MailFathom recorded against this email before asking a mail server to make them, which are
    /// removed with it.
    /// </summary>
    /// <remarks>
    /// The association is what carries a mutation history through this email's deletion path. A history of where a
    /// person's mail has been is derived personal data about that mail, so it inherits the mail's retention and deletion
    /// obligations rather than outliving it — including where the recorded mutation was the deletion itself.
    /// </remarks>
    public ICollection<MailboxMutationEntity> Mutations { get; } = [];
}
