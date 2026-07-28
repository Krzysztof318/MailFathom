// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.CodeCoverage;
using MailMcp.Domain.Emails;

namespace MailMcp.Infrastructure.Persistence;

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
    /// The snapshot records server state and is never written towards the server: MailMcp reads mail read-only, so no
    /// application path turns any of these into an IMAP <c>STORE</c>. Reconciliation refreshes them in specification 10;
    /// until then every row carries the never-observed value.
    /// </remarks>
    public DateTimeOffset? RemoteFlagsObservedAt { get; set; }

    public bool IsRemotelySeen { get; set; }

    public bool IsRemotelyAnswered { get; set; }

    public bool IsRemotelyFlagged { get; set; }

    public bool IsRemotelyDraft { get; set; }

    public bool IsRemotelyDeleted { get; set; }

    public uint ConcurrencyVersion { get; set; }

    public EmailMessageContentEntity? Content { get; set; }
}
