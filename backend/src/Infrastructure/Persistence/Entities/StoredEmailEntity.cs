// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class StoredEmailEntity
{
    /// <summary>The longest forward path RFC 5321 accepts, which bounds every address column and every address in an array.</summary>
    internal const int MaximumAddressLength = 320;

    /// <summary>The longest header line RFC 5322 accepts, which bounds every stored message identifier.</summary>
    internal const int MaximumIdentifierLength = 998;

    /// <summary>The longest domain name a resolver accepts, which bounds every sender-authentication domain column.</summary>
    internal const int MaximumDomainLength = SenderDomain.MaximumLength;

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

    /// <summary>Gets or sets the owner whose account this message belongs to.</summary>
    /// <remarks>
    /// Carried beside the account rather than reached through it, because every read of this table narrows on the
    /// owner first and an index cannot lead with a column that lives behind a join. It is written once with the
    /// row, from the account the write had already resolved, and nothing repoints an account at another owner.
    /// </remarks>
    public required Guid OwnerId { get; set; }

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

    /// <summary>
    /// Gets or sets what the receiving mail server established about who actually sent this message, which is never
    /// derived from the sender columns above.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Columns on the email rather than a table hanging off it, unlike <see cref="SpamClassification" />. The rows would
    /// not be sparse: every message whose MIME was read carries a verdict, including the not-established one that a
    /// deployment whose provider publishes no results sees on all of its mail. What reads the verdict is the arriving
    /// message's own presentation, one row at a time down a timeline, so a join per row would buy a nullable association
    /// nothing is ever without.
    /// </para>
    /// <para>
    /// The whole group is written by extraction from the stored raw MIME and is re-derivable from it, which is what a
    /// re-derivation pass does after a trusted authority is configured for an account that had none.
    /// </para>
    /// </remarks>
    public SenderAuthenticationOutcome SenderAuthenticationOutcome { get; set; }

    /// <summary>Gets or sets which check established <see cref="AuthenticatedSenderDomain" />, or that none did.</summary>
    public SenderAuthenticationMethod SenderAuthenticationMethod { get; set; }

    /// <summary>Gets or sets the domain that authenticated, which is the DKIM one wherever both checks produced one.</summary>
    public string? AuthenticatedSenderDomain { get; set; }

    /// <summary>Gets or sets the domain of a DKIM signature the server verified, absent where none verified.</summary>
    public string? DkimSignerDomain { get; set; }

    /// <summary>Gets or sets the envelope-sender domain of an SPF check that passed, absent where none passed.</summary>
    /// <remarks>
    /// Kept beside <see cref="DkimSignerDomain" /> rather than collapsed into the authenticated domain, because the two
    /// disagreeing is itself a fact about the message: a relay may be permitted to send for one domain while the
    /// signature belongs to another, and a reader asking which is which would otherwise have to re-parse the raw MIME.
    /// </remarks>
    public string? SpfMailFromDomain { get; set; }

    /// <summary>Gets or sets the domain the message displayed in <c>From</c>, absent where it wrote no usable one.</summary>
    /// <remarks>
    /// The <c>From</c> header alone, which is not the same address as <see cref="SenderAddress" />: a timeline names a
    /// message's sender from <c>Sender</c> where no author was written, while this is the domain a mail client displays
    /// and therefore the one an impersonation controls. Deriving it from the address column would be wrong for exactly
    /// those messages and would re-parse a header on a read path besides.
    /// <para>
    /// It overlaps <see cref="AuthenticatedAuthorDomain" /> and does not replace it. That column carries the domain a
    /// trusted server stood behind and is absent whenever nothing established the author, which is the case a reader
    /// most needs the displayed domain for; this one carries what the message claimed whether or not anything held.
    /// </para>
    /// </remarks>
    public string? DisplayedAuthorDomain { get; set; }

    /// <summary>Gets or sets the DMARC result the trusted header reported, or that it reported none.</summary>
    public DmarcOutcome DmarcOutcome { get; set; }

    /// <summary>Gets or sets what the receiving server established about the author the message displays.</summary>
    /// <remarks>
    /// A separate conclusion from <see cref="SenderAuthenticationOutcome" /> rather than a reading of it, because the
    /// identity that authenticated belongs to whoever handed the message over and is routinely not the displayed author
    /// at all. It is what <see cref="SenderTrustLevel" /> was decided from, so a row where the two are read together
    /// says both what was established about the author and what this deployment made of them.
    /// </remarks>
    public AuthorAuthenticationOutcome AuthorAuthenticationOutcome { get; set; }

    /// <summary>Gets or sets the displayed author's domain where it authenticated, absent where it did not.</summary>
    /// <remarks>
    /// The subject of the trust verdict below, kept beside it so a stored answer names the identity it was reached
    /// about. It is a second copy of the domain half of <see cref="SenderNormalizedAddress" /> only when the author was
    /// established, which is what makes it worth a column: the displayed address is a claim, and this is the part of it
    /// a trusted server stood behind.
    /// </remarks>
    public string? AuthenticatedAuthorDomain { get; set; }

    /// <summary>Gets or sets who reached the verdict the columns above hold.</summary>
    /// <remarks>
    /// The rest of the group says what was established; this says by whom, and the two cannot be collapsed. A receiving
    /// server saw the connection and could evaluate SPF and the sender's DMARC policy against it, while a verdict
    /// reached here after delivery rests on a signature in the stored bytes and a key its domain publishes — so
    /// <see cref="SpfMailFromDomain" /> and <see cref="DmarcOutcome" /> are empty on a locally reached row by
    /// construction rather than by outcome. It cannot be inferred from the account's configuration either, because that
    /// may have changed since the row was written.
    /// </remarks>
    public SenderAuthenticationSource SenderAuthenticationSource { get; set; }

    /// <summary>Gets or sets what this deployment made of the author the columns above establish.</summary>
    /// <remarks>
    /// Stored rather than decided when a message is read, because the trusted-sender list it was decided against
    /// changes: an operator adds a domain and a reader trusts a correspondent, and neither act may quietly rewrite the
    /// answer a message was already shown with. <see cref="SenderTrustPolicyRevision" /> is what says which list
    /// produced this row's answer.
    /// </remarks>
    public SenderTrustLevel SenderTrustLevel { get; set; }

    /// <summary>Gets or sets which half of what this deployment knows recognized the author, or that none did.</summary>
    public SenderTrustSource SenderTrustGrantedBy { get; set; }

    /// <summary>Gets or sets the trusted-sender policy the verdict was reached under, absent where no policy reached one.</summary>
    /// <remarks>
    /// Nullable because its absence is a statement: a row written before this deployment judged authors at all, and one
    /// recorded from an envelope whose payload was never stored, were judged by nothing rather than judged and left
    /// unknown.
    /// </remarks>
    public string? SenderTrustPolicyRevision { get; set; }

    /// <summary>Gets or sets how much this message's own text read as machine written.</summary>
    /// <remarks>
    /// <para>
    /// Columns on the email rather than a table hanging off it, for the reason the sender-authentication group is: every
    /// message whose MIME was read carries an answer, including the not-assessed one a deployment that turned the
    /// assessment off records on all of its mail, so a join per row would buy a nullable association nothing is ever
    /// without.
    /// </para>
    /// <para>
    /// It is an informational reading of the text and never a finding against the message. Nothing files, flags, hides,
    /// or refuses a message because of it, and no rule reads it.
    /// </para>
    /// <para>
    /// The whole group is written by extraction from the stored raw MIME and is re-derivable from it, which is what a
    /// re-derivation pass does after a release changed what the assessment reads or what it weighs.
    /// </para>
    /// </remarks>
    public MachineAuthorshipBand MachineAuthorshipBand { get; set; }

    /// <summary>Gets or sets the likelihood the band above is the reading of, from zero to one inclusive.</summary>
    /// <remarks>
    /// Zero where nothing read the text, which is indistinguishable from a text that was read and carried no signal —
    /// deliberately, because <see cref="MachineAuthorshipBand" /> is what separates the two and a second column saying
    /// the same thing would be a second place to keep right.
    /// </remarks>
    public double MachineAuthorshipLikelihood { get; set; }

    /// <summary>Gets or sets the set of signals the text carried, which is what the likelihood was computed from.</summary>
    /// <remarks>
    /// Stored as the integer the flag set is rather than as the text every other enum here is stored as. A set written
    /// as text is a formatted list rather than a value: no query can ask which rows carry one member of it, and reading
    /// one back depends on the separator that wrote it. The members are explicit powers of two that are never reordered
    /// or reused, which is what makes the numeric form safe.
    /// </remarks>
    public MachineAuthorshipSignals MachineAuthorshipSignals { get; set; }

    /// <summary>Gets or sets the weighting the likelihood was reached under, absent where nothing assessed the message.</summary>
    /// <remarks>
    /// Nullable because its absence is a statement: a row written before this deployment assessed authorship, one whose
    /// body yielded no words, and one recorded while the assessment was turned off were all judged by nothing rather
    /// than judged and found ordinary.
    /// </remarks>
    public string? MachineAuthorshipProfileRevision { get; set; }

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

    /// <summary>
    /// Gets or sets the conversation the three identifiers above placed this email in, or <see langword="null" /> while
    /// nothing has placed it.
    /// </summary>
    /// <remarks>
    /// Nullable because its absence is a statement about this deployment rather than about the message: a row stored
    /// before this release was assembled into nothing, and stays that way until <c>mfctl mailbox rederive</c> re-reads
    /// it. Every arrival from this release onwards is assigned in the transaction that commits it, so the column is
    /// absent on old mail and present on new.
    /// </remarks>
    public Guid? EmailThreadId { get; set; }

    /// <summary>
    /// Gets or sets the stored email this one answers, or <see langword="null" /> when it answers none this deployment
    /// holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The edge <see cref="InReplyTo" /> names, resolved to a row. It is what makes a thread a tree rather than a set,
    /// and it is stored rather than re-derived on every read because re-deriving it would mean matching identifier
    /// strings across a thread's rows each time anybody opened one.
    /// </para>
    /// <para>
    /// A message whose named ancestor is not stored here carries none and is a root of its thread, which is the honest
    /// answer rather than a gap: nothing local knows what sits above it. A relation that would close a cycle is refused
    /// rather than written, because an order cannot be produced from one.
    /// </para>
    /// </remarks>
    public Guid? ParentStoredEmailId { get; set; }

    public int AttachmentCount { get; set; }

    public long AttachmentTotalSizeOctets { get; set; }

    public int InlineResourceCount { get; set; }

    public bool IsEncrypted { get; set; }

    /// <summary>Gets or sets whether a signature part is present. Nothing here has verified it, and the name says so.</summary>
    public bool CarriesUnverifiedSignature { get; set; }

    public bool ContainsUnexpandedTnefPart { get; set; }

    /// <summary>Gets or sets when this occurrence was first recorded locally.</summary>
    /// <remarks>
    /// <para>
    /// A statement about this deployment rather than about the message, which is why it is neither
    /// <see cref="SentAt" /> nor <see cref="ReceivedAt" />: both of those are what the mail server said, and an initial
    /// synchronization of a ten-year-old mailbox stores a decade of them in one afternoon.
    /// </para>
    /// <para>
    /// What reads it is the wait a spam verdict is allowed. Derived work is ordered behind classification, so a message
    /// with no verdict is held — and the only thing that distinguishes one still waiting from one nothing is ever going
    /// to reach is how long it has been here. Writing it once and never updating it is what makes that a wait rather
    /// than a value a later write could reset.
    /// </para>
    /// </remarks>
    public DateTimeOffset StoredAt { get; set; }

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

    /// <summary>
    /// Gets or sets whether this email is kept readable although its remote occurrence has gone, which only a delete
    /// MailFathom performed under <see cref="AuthoredDeleteEmailDisposition.RetainLocalCopy" /> sets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is what separates freeing space on the server from forgetting the mail. The row still carries
    /// <see cref="RemoteExpungeObservedAt" />, because the server genuinely no longer holds the message and the
    /// reconciliation queue must stop asking about it; this flag is what keeps the same row inside every mailbox query
    /// the timestamp would otherwise take it out of.
    /// </para>
    /// <para>
    /// Nothing but a change MailFathom itself authored sets it: a delete, or a relocation into a folder MailFathom does
    /// not mirror, which is the same loss of the occurrence and is answered by the same setting. A disappearance somebody
    /// else caused is answered by <see cref="RemotelyDeletedEmailDisposition" />, which has no value that keeps the mail
    /// readable: MailFathom did not cause that removal and cannot say the owner meant to keep a local copy of it. The
    /// name says "authored delete" because that is the setting's name and the case it was written for; a relocation out
    /// of the mirrored mailbox joined it rather than earning a column of its own.
    /// </para>
    /// </remarks>
    public bool IsRetainedAfterAuthoredDelete { get; set; }

    /// <summary>Gets or sets the outgoing record this email is MailFathom's own filed copy of, and <see langword="null" /> for every message it did not send.</summary>
    /// <remarks>
    /// <para>
    /// A copy MailFathom appends to the sent or outbox folder comes back through synchronization as ordinary new mail,
    /// and this is what tells it apart afterwards. It is the join everything reacting to newly synchronized mail filters
    /// on: a rule conditioned on arriving mail must not fire on the owner's own outgoing message the moment its copy is
    /// discovered.
    /// </para>
    /// <para>
    /// It is a plain column rather than a foreign key, for the reason the outgoing record's own account column is one:
    /// the two rows have different lifetimes, and an outgoing record erased under its retention obligation must not take
    /// the mail out of the mailbox with it.
    /// </para>
    /// </remarks>
    public Guid? FiledFromOutgoingEmailId { get; set; }

    public bool IsRemotelySeen { get; set; }

    public bool IsRemotelyAnswered { get; set; }

    public bool IsRemotelyFlagged { get; set; }

    public bool IsRemotelyDraft { get; set; }

    public bool IsRemotelyDeleted { get; set; }

    /// <summary>
    /// Gets or sets the keywords the server reported beside the five flags above, in the normalized form
    /// <see cref="RemoteEmailKeywords" /> produces, which is the form a keyword filter matches on.
    /// </summary>
    /// <remarks>
    /// An empty array is what both an email carrying no keyword and an email nobody has observed hold, which is the same
    /// ambiguity the five booleans carry and is resolved by the same timestamp. The column sits on the email's own row
    /// rather than in a table of its own, so every tombstone, retention, erasure, and export path that already carries
    /// the row carries the keywords with it and none of them gains a second thing to account for.
    /// </remarks>
    public string[] RemoteKeywords { get; set; } = [];

    /// <summary>
    /// Gets or sets when a rule pass last evaluated this email, or <see langword="null" /> while none has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The column is the arrival queue. A pass reads the account's rows carrying no value, in identity order, and
    /// writing one is what takes a row out of the queue — so a rule applies to mail arriving from now on rather than to
    /// a mailbox's whole history, and running the rules over mail already stored is something an owner asks for.
    /// </para>
    /// <para>
    /// The migration that adds it stamps every row already stored, which is the same statement made about the mail that
    /// existed before rules ran at all: an upgrade must not turn a first rule set loose on years of correspondence.
    /// </para>
    /// </remarks>
    public DateTimeOffset? RulesEvaluatedAt { get; set; }

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
    /// Gets or sets what classification concluded about this email, which is absent until a classification has run for
    /// it and stays absent on a deployment that never switches classification on.
    /// </summary>
    /// <remarks>
    /// Derived data hanging off the occurrence rather than columns on it, which is what makes it removed with the mail
    /// it describes and absent — rather than null-valued on every row — where the feature is off.
    /// </remarks>
    public EmailSpamClassificationEntity? SpamClassification { get; set; }

    /// <summary>
    /// Gets or sets the length this email's extracted text had when the per-message embedding ceiling stopped the cut
    /// short of its end, or <see langword="null" /> when no ceiling reached it.
    /// </summary>
    /// <remarks>
    /// Recorded rather than inferred. A message cut whole and one cut to a ceiling are indistinguishable from their
    /// passages alone, so without this column the question "could the answer have been in the part nobody embedded"
    /// would be answered by guessing from a chunk count. The value is the length of the text, which against the ceiling
    /// in force says exactly how much was left out.
    /// </remarks>
    public int? ChunkedTextTruncatedFromCharacterCount { get; set; }

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
